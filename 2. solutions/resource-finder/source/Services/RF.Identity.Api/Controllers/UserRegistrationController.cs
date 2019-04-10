﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RF.Identity.Api.Domain.Enums;
using RF.Identity.Api.Domain.Requests;
using RF.Identity.Api.Domain.Results;
using RF.Identity.Api.Domain.Settings;
using RF.Identity.Api.Helpers.Data;
using RF.Identity.Api.Helpers.Queue;
using RF.Identity.Domain.Entities.Data;
using RF.Identity.Domain.Entities.KeyVault;
using RF.Identity.Domain.Entities.Queue;
using RF.Identity.Domain.Enums;
using RF.Identity.Domain.Exceptions;
using System;
using System.Threading.Tasks;

namespace RF.Identity.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserRegistrationController : ControllerBase
    {
        private readonly ILogger logger = null;

        public UserRegistrationController(ILogger<UserRegistrationController> logger)
        {
            this.logger = logger;
        }

        [HttpPost]
        [Authorize(Policy = "RF-Users")]
        public async Task<IActionResult> Post()
        {
            // non-forced-to-disposal
            UserRegistrationResult result = new UserRegistrationResult
            {
                IsSucceded = true,
                ResultId = (int)UserRegistrationResultEnum.Success
            };

            // forced-to-disposal
            UserRegistrationRequest userRegistrationRequest = null;
            MongoDBConnectionInfo mongoDBConnectionInfo = null;
            KeyVaultConnectionInfo keyVaultConnectionInfo = null;
            User user = null;

            try
            {
                userRegistrationRequest = new UserRegistrationRequest();
                userRegistrationRequest.Email = HttpContext.User.FindFirst("preferred_username").Value;
                userRegistrationRequest.Fullname = ((HttpContext.User.FindFirst("name") != null) ? HttpContext.User.FindFirst("name").Value : string.Empty);

                if (string.IsNullOrEmpty(userRegistrationRequest.Fullname))
                    throw new BusinessException((int)UserRegistrationResultEnum.FailedEmptyFullname);

                mongoDBConnectionInfo = new MongoDBConnectionInfo()
                {
                    ConnectionString = ApplicationSettings.ConnectionString,
                    DatabaseId = ApplicationSettings.DatabaseId,
                    UserCollection = ApplicationSettings.UserCollection
                };

                using (UserRegistrationDataHelper userRegistrationDataHelper = new UserRegistrationDataHelper(mongoDBConnectionInfo))
                {
                    user = userRegistrationDataHelper.GetUser(userRegistrationRequest.Email);
                }

                if (user == null)
                {
                    keyVaultConnectionInfo = new KeyVaultConnectionInfo()
                    {
                        CertificateName = ApplicationSettings.KeyVaultCertificateName,
                        ClientId = ApplicationSettings.KeyVaultClientId,
                        ClientSecret = ApplicationSettings.KeyVaultClientSecret,
                        KeyVaultIdentifier = ApplicationSettings.KeyVaultIdentifier
                    };

                    using (MessageQueueHelper messageQueueHelper = new MessageQueueHelper())
                    {
                        UserRegistrationMessage userRegistrationMessage = new UserRegistrationMessage()
                        {
                            fullname = userRegistrationRequest.Fullname,
                            email = userRegistrationRequest.Email
                        };

                        await messageQueueHelper.QueueMessageAsync(userRegistrationMessage, ApplicationSettings.UserRegistrationQueueName, keyVaultConnectionInfo);
                    }
                }
                else
                {
                    result.ResultId = (int)UserRegistrationResultEnum.SuccessAlreadyExists;
                }
            }
            catch (Exception ex)
            {
                result.IsSucceded = false;

                if (ex is BusinessException)
                {
                    result.ResultId = ((BusinessException)ex).ResultId;
                }
                else
                {
                    result.ResultId = (int)UserRegistrationResultEnum.Failed;

                    this.logger.LogError($">> Exception: {ex.Message}, StackTrace: {ex.StackTrace}");

                    if (ex.InnerException != null)
                    {
                        this.logger.LogError($">> Inner Exception Message: {ex.InnerException.Message}, Inner Exception StackTrace: {ex.InnerException.StackTrace}");
                    }
                }
            }
            finally
            {
                userRegistrationRequest = null;
                mongoDBConnectionInfo = null;
                keyVaultConnectionInfo = null;
                user = null;

                GC.Collect();
            }

            string message = EnumDescription.GetEnumDescription((UserRegistrationResultEnum)result.ResultId);
            this.logger.LogInformation($">> Message information: {message}");

            return (result.IsSucceded) ? (ActionResult)new OkObjectResult(new { message = message }) : (ActionResult)new BadRequestObjectResult(new { message = message });
        }
    }
}