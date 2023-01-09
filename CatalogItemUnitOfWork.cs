using System;
using System.Threading.Tasks;
using System.Transactions;
using Cloud.Catalog.Microservice.AppCore.Common.Interfaces.Common;
using Cloud.Catalog.Microservice.AppCore.Common.Interfaces.Repositories;
using Cloud.Catalog.Microservice.AppCore.Common.Interfaces.UnitsOfWork;
using Cloud.Catalog.Microservice.Domain.Entities;
using Cloud.Catalog.Microservice.Domain.Interfaces;
using Cloud.Common.Infrastructure.Dapper.Services;
using Cloud.Common.Infrastructure.Sql.Builders;
using Cloud.Common.Infrastructure.Sql.Configuration;
using MediatR;

namespace Cloud.Catalog.Microservice.Infrastructure.AzureDb.UnitOfWorks
{
    public abstract class CatalogItemUnitOfWork<T> : ICatalogItemUnitOfWork<T> where T : CatalogItem
    {
        private readonly IReliableDataAccessorService<SqlConnectionConfiguration> dbAccessor;
        private readonly IQueryBuilder queryBuilder;
        private readonly ICatalogItemRepository catalogItemRepository;
        private readonly ICatalogItemRepository<T> specificItemRepository;
        private readonly IDomainEventsDispatcher domainEventsDispatcher;

        public ICatalogItemRepository CatalogItemRepository => catalogItemRepository;
        public ICatalogItemRepository<T> SpecificItemRepository => specificItemRepository;

        public CatalogItemUnitOfWork(
            IReliableDataAccessorService<SqlConnectionConfiguration> dbAccessor,
            IQueryBuilder queryBuilder,
            ICatalogItemRepository catalogItemRepository,
            ICatalogItemRepository<T> specificItemRepository,
            IDomainEventsDispatcher domainEventsDispatcher)
        {
            this.dbAccessor = dbAccessor;
            this.queryBuilder = queryBuilder;
            this.catalogItemRepository = catalogItemRepository;
            this.specificItemRepository = specificItemRepository;
            this.domainEventsDispatcher = domainEventsDispatcher;
        }

        public async Task<Guid> AddAsync(T item)
        {
            var result = await DoAndDispatchEventsAsync(async () =>
            {
                return await specificItemRepository.AddAsync(item);
            }, item);
            return result;
        }

        public async Task<bool> UpdateAsync(T item)
        {
            var result = await DoAndDispatchEventsAsync(async () =>
            {
                return await specificItemRepository.UpdateAsync(item);
            }, item);
            return result;
        }

        public async Task<bool> UpsertAsync(T item)
        {
            var result = await DoModificationAndDispatchEventsAsync(async () =>
            {
                var (isUpserted, isInserted) = await specificItemRepository.UpsertAsync(item);

                bool isUpdated = isUpserted && !isInserted;
                if (isUpdated)
                {
                    await catalogItemRepository.ActivateDeactivateAsync(item);
                }

                return isUpserted;

            }, item);
            return result;
        }

        public async Task<Unit> ActivateDeactivateAsync(CatalogItem item)
        {
            await DoAndDispatchEventsAsync(async () =>
            {
                return await catalogItemRepository.ActivateDeactivateAsync(item);
            }, item);
            return Unit.Value;
        }

        public async Task<Unit> DeleteAsync(CatalogItem item)
        {
            await DoModificationAndDispatchEventsAsync(async () =>
            {
                return await catalogItemRepository.DeleteAsync(item);
            }, item);
            return Unit.Value;
        }

        public async Task<Unit> RestoreAsync(CatalogItem item)
        {
            await DoAndDispatchEventsAsync(async () =>
            {
                return await catalogItemRepository.RestoreDeletedAsync(item);
            }, item);
            return Unit.Value;
        }

        public async Task<CatalogItem> GetByKeyAsync(Guid practiceKey, Guid catalogItemKey)
        {
            var item = await specificItemRepository.GetByKeyAsync(catalogItemKey);
            return item;
        }

        public Task<bool> IsLinkedItem(Guid itemKey, Guid practiceKey)
        {
            return SpecificItemRepository.IsLinkedItem(itemKey, practiceKey);
        }

        private async Task<TR> DoAndDispatchEventsAsync<TR>(Func<Task<TR>> action, CatalogItem item)
        {
            TR result;

            using (var scope = TransactionScopeBuilder.CreateReadCommitted())
            {
                result = await action();
                await domainEventsDispatcher.DispatchEventsAsync(item);
                scope.Complete();
            }

            return result;
        }

        private async Task<bool> DoModificationAndDispatchEventsAsync(Func<Task<bool>> action, CatalogItem item)
        {
            bool isModified;

            using (var scope = TransactionScopeBuilder.CreateReadCommitted())
            {
                isModified = await action();
                
                if (isModified)
                {
                    await domainEventsDispatcher.DispatchEventsAsync(item);
                    scope.Complete();
                }
            }

            return isModified;
        }
    }
}
