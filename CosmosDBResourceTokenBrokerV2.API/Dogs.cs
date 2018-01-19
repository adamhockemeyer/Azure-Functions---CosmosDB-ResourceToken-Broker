
using System;
using System.IO;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;

using CosmosDBResourceTokenBroker.Shared;
using CosmosDBResourceTokenBroker.Shared.Models;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents;

namespace CosmosDBResourceTokenBrokerV2.API
{
    /*
     * Notes:  This Function is for demonstation purposes to act as a 'Client' that would be using the CosmosDB SDK directly
     * in conjunction with using a resource key for data access protection.  Typically you make similar calls as below
     * directly from the native client app (Console, Xamarin, etc.) instead of a REST call.
     * 
     */

    public static class Dogs
    {
        private static string cosmosDatabase = GetEnvironmentVariable("cosmosDatabase");
        private static string cosmosCollection = GetEnvironmentVariable("cosmosCollection");

        /// <summary>
        /// Setup our repository with our connection info.  These can be changed at any time
        /// by using the Fluent syntax.
        /// </summary>
        static CosmosDBRepository repo = CosmosDBRepository.Instance
                .Endpoint(GetEnvironmentVariable("cosmosDBEndpoint"))
                .Database(cosmosDatabase)
                .Collection(cosmosCollection);

        static System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();


        /// <summary>
        /// Add a dog using your request token.
        /// </summary>
        /// <param name="req"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("AddDogs")]
        public static async Task<IActionResult> AddDog([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)]HttpRequest req, TraceWriter log)
        {
            sw.Restart();

            string dogName = req.Query["Name"];
            string dogBreed = req.Query["Breed"];
            string userId = req.Query["UserId"];

            string resourceToken = req.Headers["ResourceToken"];

            if (string.IsNullOrEmpty(resourceToken))
            {
                new BadRequestObjectResult("ResourceToken is required");
            }

            // Set the resource token, to demonstrate usage from a 'Client'.
            repo.AuthKeyOrResourceToken(resourceToken);
            // Set the partition key, so the user has access to their documents, based on the permission that was setup
            // by using the userid as a permission key.  A client could just set this once initially.
            repo.PartitionKey(userId);

            Dog dog = await repo.UpsertItemAsync<Dog>(new Dog { Breed = dogBreed, Name = dogName });

            sw.Stop();

            log.Info($"Execution took: {sw.ElapsedMilliseconds}ms.");

            return dog != null
                ? (ActionResult)new OkObjectResult(dog)
                : new BadRequestObjectResult("Unable to add the dog.");
        }


        /// <summary>
        /// This example shows how a client can only access items that the resource token and their permission key match.
        /// </summary>
        /// <param name="req"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("GetMyDogs")]
        public static async Task<IActionResult> GetMyDogs([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)]HttpRequest req, TraceWriter log)
        {
            sw.Restart();

            // As a client, you would already have your userId when calling typically.
            string userId = req.Query["UserId"];

            string resourceToken = req.Headers["ResourceToken"];

            if (string.IsNullOrEmpty(resourceToken))
            {
                new BadRequestObjectResult("ResourceToken is required");
            }

            // Set the resource token, to demonstrate usage from a 'Client'.
            repo.AuthKeyOrResourceToken(resourceToken);
            // Set the parition key, since our resource token is limited by partition key.  A client could just set this once initially.
            repo.PartitionKey(userId);

            var results = await repo.GetAllItemsAsync<Dog>(new FeedOptions { PartitionKey = new PartitionKey("adhockem@microsoft.com") });

            sw.Stop();
            log.Info($"Execution took: {sw.ElapsedMilliseconds}ms.");

            return results != null
                ? (ActionResult)new OkObjectResult(results)
                : new BadRequestObjectResult("Unable to find document(s) with the given type.");
        }

        /// <summary>
        /// Example shows how a user with a given resource token cannot access items their permission does not have the appropriate partition key.
        /// </summary>
        /// <param name="req"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("TryGetAllDogs")]
        public static async Task<IActionResult> TryGetAllDogs([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)]HttpRequest req, TraceWriter log)
        {
            sw.Restart();

            // As a client, you would already have your userId when calling typically.
            string userId = req.Query["UserId"];

            string resourceToken = req.Headers["ResourceToken"];

            if (string.IsNullOrEmpty(resourceToken))
            {
                new BadRequestObjectResult("ResourceToken is required");
            }

            // Set the resource token, to demonstrate usage from a 'Client'.
            repo.AuthKeyOrResourceToken(resourceToken);
            // Set the parition key, since our resource token is limited by partition key.  A client could just set this once initially.
            repo.PartitionKey(userId);

            // BUG: This seems to fail on Azure Functions V2 due to the following: 
            // https://github.com/Azure/azure-documentdb-dotnet/issues/202
            // https://github.com/Azure/azure-documentdb-dotnet/issues/312
            var results = await repo.GetAllItemsAsync<Dog>(new FeedOptions { EnableCrossPartitionQuery = true });

            sw.Stop();
            log.Info($"Execution took: {sw.ElapsedMilliseconds}ms.");

            return results != null
                ? (ActionResult)new OkObjectResult(results)
                : new BadRequestObjectResult("Unable to find document(s) with the given type.");
        }

        public static string GetEnvironmentVariable(string name)
        {
            return System.Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        }
    }
}
