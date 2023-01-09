using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Cloud.Catalog.Common.Enums;
using Cloud.Catalog.Microservice.AppCore.Common.Commands.Handlers;
using Cloud.Catalog.Microservice.AppCore.Common.Interfaces.Common;
using Cloud.Catalog.Microservice.AppCore.Common.Interfaces.Repositories;
using Cloud.Catalog.Microservice.AppCore.Common.Interfaces.Services;
using Cloud.Catalog.Microservice.AppCore.Common.Interfaces.UnitsOfWork;
using Cloud.Catalog.Microservice.AppCore.Common.Interfaces.Validators;
using Cloud.Catalog.Microservice.AppCore.Common.Models;
using Cloud.Catalog.Microservice.AppCore.Common.Models.Service;
using Cloud.Catalog.Microservice.Domain.Entities;
using FluentValidation;

namespace Cloud.Catalog.Microservice.AppCore.Services.Commands.Handlers
{
    /// <summary>
    /// Handler responsible for creating new service
    /// </summary>
    public class CreateServiceCommandHandler : CreateCatalogItemCommandHandler<CreateServiceCommand, ServiceDto, Service>
    {
        private readonly IServiceDomainValidator serviceDomainValidator;

        /// <summary>
        /// CreateServiceCommandHandler constructor
        /// </summary>
        /// <param name="mapper">Mapper instance</param>
        /// <param name="categoryRepository">The category repository.</param>
        /// <param name="userService">The user repository.</param>
        /// <param name="taxLevelRepository">The tax level repository</param>
        /// <param name="serviceFeeRepository">The service fee repository.</param>
        /// <param name="localizer">The localizer.</param>
        public CreateServiceCommandHandler(
            IMapper mapper,
            IServiceDomainValidator serviceDomainValidator,
            ICategoryRepository categoryRepository,
            IContextUserService userService,
            ITaxLevelRepository taxLevelRepository,
            IServiceFeeRepository serviceFeeRepository,
            IDocumentTemplateRepository documentTemplateRepository,
            IServiceUnitOfWork catalogItemUnitOfWork,
            ILocalizerService localizer) : base(
                mapper,
                categoryRepository,
                userService,
                taxLevelRepository,
                documentTemplateRepository,
                serviceFeeRepository,
                catalogItemUnitOfWork,
                localizer)
        {
            this.serviceDomainValidator = serviceDomainValidator;
        }

        /// <summary>
        /// Handles service creation
        /// </summary>
        /// <param name="command">Command that contains required parameters for service creation</param>
        /// <param name="cancellationToken"></param>
        /// <returns>Created service</returns>
        public override Task<ServiceDto> Handle(CreateServiceCommand command, CancellationToken cancellationToken)
        {
            ValidateCommand(command);

            var itemType = GetItemType();

            var isCatalogItemExists = await catalogItemUnitOfWork.SpecificItemRepository.ExistsAsync(itemType, command.Code, command.PracticeKey);
            if (isCatalogItemExists)
            {
                throw new EntityAlreadyExistsException(typeof(TEntity).Name);
            }

            if (command.CatalogItemKey.HasValue)
            {
                isCatalogItemExists = await catalogItemUnitOfWork.SpecificItemRepository.ExistsAsync(command.CatalogItemKey.Value);
                if (isCatalogItemExists)
                {
                    throw new EntityAlreadyExistsException(typeof(TEntity).Name);
                }
            }

            var category = await categoryRepository.GetByKeyAsync(command.CategoryKey);
            if (!(category is null) && itemType != category.ItemType)
            {
                throw new DomainValidationException(localizer.Get(LocalStrings.IncorrectCategoryType), ErrorCode.IncorrectCategoryTypeForBillableItem);
            }

            var commandItem = mapper.Map<TCommand, TEntity>(command);

            commandItem.SetPriceDetails(new ItemPriceDetails(command.Cost, command.BasePrice, command.Markup, command.DiscountPercent));

            await SetTierPrices(command, commandItem);
            SetSpecies(command, commandItem);
            await SetReminders(command, commandItem);

            var user = await GetUser(command);

            //fill out user id to use in SetCategory
            SetUser(commandItem, user);

            await Task.WhenAll(new[] {
                SetTaxLevel(command, commandItem),
                SetCategory(command, commandItem),
                SetServiceFees(command, commandItem),
                SetSpecificProperties(command, commandItem),
                SetLinkedItemsAsync(command, commandItem),
                SetDocumentTemplate(command, commandItem)
            });

            var newItem = (TEntity)CreateNew(command, user).UpdateFrom(commandItem, user);
            var createdItem = await HandleSave(newItem);

            return mapper.Map<TEntity, TResponse>(createdItem);
        }

        /// <summary>
        /// Adds the specific service properties.
        /// </summary>
        /// <param name="entity">The service.</param>
        /// <param name="command">The command.</param>
        /// <returns></returns>
        protected override Task SetSpecificProperties(CreateServiceCommand command, Service entity)
        {
            if (entity.Vaccine != null)
            {
                entity.Vaccine.VaccineKey = Guid.NewGuid();
                entity.Vaccine.CreatedDate = DateTime.UtcNow;
                entity.Vaccine.ModifiedDate = DateTime.UtcNow;
                entity.Vaccine.SetCreatedByUser(entity.CreatedByUser);
                entity.Vaccine.SetModifiedByUser(entity.ModifiedByUser);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Specifics the validation for service.
        /// </summary>
        /// <param name="command">The command.</param>
        /// <returns></returns>
        /// <exception cref="ValidationException"></exception>
        protected override void SpecificValidation(CreateServiceCommand command)
        {
            serviceDomainValidator.ValidateVaccine(command);
        }

        /// <summary>
        /// Adds linked items to catalog item
        /// </summary>
        /// <param name="command">The command</param>
        /// <param name="entity">Entity where to add linked item</param>
        /// <returns></returns>
        protected override async Task SetLinkedItemsAsync(CreateServiceCommand command, Service entity)
        {
            MapLinkedItemsAsync(command, entity);

            if (entity.LinkedItems.Count > 1)
            {
                throw new ArgumentException(localizer.Get(LocalStrings.MaximumLinkedItemsError, "1"));
            }

            foreach (var linkedItem in entity.LinkedItems)
            {
                var linkedCatalogItem = await catalogItemUnitOfWork.CatalogItemRepository.GetByKeyAsync(entity.PracticeKey, linkedItem.LinkedCatalogItemKey);

                serviceDomainValidator.ValidateLinkedItem(linkedCatalogItem);

                linkedItem.LinkedCatalogItemId = linkedCatalogItem.Id;
                linkedItem.ItemType = linkedCatalogItem.ItemType;
                linkedItem.Code = linkedCatalogItem.Code;
            }
        }

        protected override ItemType GetItemType() => ItemType.Service;

        protected override Service CreateNew(CreateServiceCommand command, ContextUser user)
        {
            return new Service(
                command.PracticeKey,
                new Category(command.CategoryKey),
                user,
                new TaxLevel(command.PracticeKey, command.TaxLevelKey, command.TaxLevel?.TaxLevelValue),
                command.Code,
                command.InternalName,
                0)
            {
                CreatedDate = command.ModifiedDate,
                ModifiedDate = command.ModifiedDate
            };
        }
    }
}
