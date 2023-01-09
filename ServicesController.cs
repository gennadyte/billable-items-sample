using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Authorization.Services.Infrastructure.Attributes;
using Authorization.Services.Models;
using AutoMapper;
using Cloud.Catalog.Common.Contracts.Request.Services;
using Cloud.Catalog.Common.Contracts.Response;
using Cloud.Catalog.Microservice.API.Infrastructure.ActionFilters;
using Cloud.Catalog.Microservice.API.Infrastructure.Models;
using Cloud.Catalog.Microservice.AppCore.Common.Interfaces.Common;
using Cloud.Catalog.Microservice.AppCore.Common.Models.Service;
using Cloud.Catalog.Microservice.AppCore.Labs.Commands;
using Cloud.Catalog.Microservice.AppCore.Services.Commands;
using Cloud.Catalog.Microservice.AppCore.Services.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

[assembly: InternalsVisibleTo("Cloud.Catalog.Microservice.API.Tests")]

namespace Cloud.Catalog.Microservice.API.Controllers
{
    /// <summary>
    /// Handles all services related requests
    /// </summary>
    /// <seealso cref="ControllerBase" />
    [ApiController]
    [ServiceFilter(typeof(ValidatePracticeByPracticeKeyInRoute))]
    [Produces("application/json")]
    [Route("api/v{version:apiVersion}/Catalog/Practices/{practiceKey}/[controller]")]
    [ApiVersion("1.0")]
    [Authorize(Policy = Schemas.AUTHZ)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public class ServicesController : ControllerBase
    {
        private readonly IMediator mediator;
        private readonly IMapper mapper;
        private readonly ILocalizerService localizer;

        /// <summary>
        /// Initializes a new instance of the <see cref="ServicesController"/> class.
        /// </summary>
        /// <param name="mediator">The mediator.</param>
        /// <param name="mapper">The mapper.</param>
        /// <param name="localizer">The localizer.</param>
        public ServicesController(IMediator mediator, IMapper mapper, ILocalizerService localizer)
        {
            this.mediator = mediator;
            this.mapper = mapper;
            this.localizer = localizer;
        }

        /// <summary>
        /// Looks for a service with specified key.
        /// </summary>
        /// <param name="practiceKey">Key of the practice to look in.</param>
        /// <param name="serviceKey">Key of the service to look for.</param>
        /// <param name="includeDeleted">Include deleted service</param>
        /// <returns>Service with specified key.</returns>
        [HttpGet]
        [Route("{serviceKey}")]
        [ProducesResponseType(typeof(SingleEntityResponse<ServiceResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
        [PermissionsAuthorization(Permissions.ACCESS_BILLABLECloud)]
        public async Task<ActionResult<SingleEntityResponse<ServiceResponse>>> GetByKey([FromRoute] Guid practiceKey, [FromRoute] Guid serviceKey, [FromQuery] bool includeDeleted)
        {
            var service = await mediator.Send(new GetServiceByKeyQuery(practiceKey, serviceKey, includeDeleted, localizer));
            var response = mapper.Map<ServiceResponse>(service);
            return Ok(new SingleEntityResponse<ServiceResponse>(response));
        }

        /// <summary>
        /// Creates a new service for the specified practice.
        /// </summary>
        /// <param name="practiceKey">Key of the practice to look in.</param>
        /// <param name="request">The create service request.</param>
        /// <returns>Created service</returns>
        [HttpPost]
        [ProducesResponseType(typeof(SingleEntityResponse<CreateServiceResponse>), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
        [PermissionsAuthorization(Permissions.MANAGE_PROCEDURES)]
        public async Task<ActionResult> Create(
            [FromRoute] Guid practiceKey,
            [FromBody] CreateServiceRequest request)
        {
            var command = mapper.Map<CreateServiceRequest, CreateServiceCommand>(request);
            command.PracticeKey = practiceKey;
            var service = await mediator.Send(command);
            var response = mapper.Map<ServiceDto, CreateServiceResponse>(service);

            return CreatedAtAction(nameof(GetByKey), new { practiceKey, serviceKey = response.CatalogItemKey }, new SingleEntityResponse<CreateServiceResponse>(response));
        }

        /// <summary>
        /// Updates service in specified practice.
        /// </summary>
        /// <param name="practiceKey">Key of the practice to look in.</param>
        /// <param name="serviceKey">The identifier of the service.</param>
        /// <param name="request">Body of the request.</param>
        /// <returns></returns>
        [HttpPut("{serviceKey}")]
        [ProducesResponseType(typeof(SingleEntityResponse<ServiceResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
        [PermissionsAuthorization(Permissions.MANAGE_PROCEDURES)]
        public async Task<ActionResult<SingleEntityResponse<ServiceResponse>>> Update(
            [FromRoute] Guid practiceKey,
            [FromRoute] Guid serviceKey,
            [FromBody] UpdateServiceRequest request)
        {
            var command = mapper.Map<UpdateServiceRequest, UpdateServiceCommand>(request);

            command.PracticeKey = practiceKey;
            command.CatalogItemKey = serviceKey;

            var data = await mediator.Send(command);
            var result = mapper.Map<ServiceDto, ServiceResponse>(data);

            return Ok(new SingleEntityResponse<ServiceResponse>(result));
        }

        /// <summary>
        /// Converts Service to Lab.
        /// </summary>
        /// <param name="practiceKey">The practice key.</param>
        /// <param name="serviceKey">The service key.</param>
        /// <param name="request">The request payload.</param>
        /// <param name="force">
        /// Indicates whether the convertation should occur
        /// when the service is being referenced by another item.
        /// </param>
        [HttpPut("{serviceKey}/convert")]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(SingleEntityResponse<ServiceToLabResponse>), StatusCodes.Status200OK)]
        [PermissionsAuthorization(Permissions.MANAGE_PROCEDURES)]
        public async Task<ActionResult<SingleEntityResponse<ServiceToLabResponse>>> Convert(
            [FromBody]  ServiceToLabRequest request,
            [FromRoute] Guid practiceKey,
            [FromRoute] Guid serviceKey,
            [FromQuery] bool force = false)
        {
            var command = mapper.Map<ServiceToLabCommand>(request);
            command.CatalogItemKey = serviceKey;
            command.PracticeKey = practiceKey;
            command.Force = force;

            var data = await mediator.Send(command);
            var result = mapper.Map<ServiceToLabResponse>(data);

            return Ok(new SingleEntityResponse<ServiceToLabResponse>(result));
        }
    }
}
