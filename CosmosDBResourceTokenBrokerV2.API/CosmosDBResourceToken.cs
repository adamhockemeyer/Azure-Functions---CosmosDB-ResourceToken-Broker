
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using System;
using CosmosDBResourceTokenBroker.Shared;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace CosmosDBResourceTokenBrokerV2.API
{
    public static class CosmosDBResourceToken
    {
        private static string HOST = GetEnvironmentVariable("host");
        private static string DATABASE = GetEnvironmentVariable("cosmosDatabase");
        private static string COLLECTION = GetEnvironmentVariable("cosmosCollection");
        private static TimeSpan TOKEN_EXPIRY = TimeSpan.FromHours(5);  // Resource Token defaults to 1 hour, max of 5 hours.

        static System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        private static readonly Lazy<HttpClient> _http = new Lazy<HttpClient>(() => new HttpClient());
        private static HttpClient http => _http.Value;

        // Using our repository instead of CosmosDB Bindings.
        static CosmosDBRepository repo = CosmosDBRepository.Instance
                .ConnectionString(GetEnvironmentVariable("myCosmosDB"))
                .Database(DATABASE)
                .Collection(COLLECTION);

        [FunctionName("CosmosDBResourceToken")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)]HttpRequest req, TraceWriter log)
        {
            sw.Restart();

            PermissionToken permissionToken = null;

            string authHeader = req.Headers["Authorization"];

            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer"))
            {
                // User passed an authenication token
                // Process and create a user in CosmosDB, write the token to the token cache and return the resource token.

                string accessToken = authHeader.Replace("Bearer ", "").Trim();
                string userId = await GetUserIDFromAccessToken(HOST, accessToken);

                if (string.IsNullOrEmpty(userId))
                {
                    return new BadRequestObjectResult("Unable to get userId from the token.");
                }

                permissionToken = await GetPermissionToken(userId, repo, PermissionMode.All);
            }
            else
            {
                // Anonymous User
                return new BadRequestObjectResult("The request does not contain an OAuth Authorization Bearer token.");
            }

            sw.Stop();

            log.Info($"Request Duration: {sw.ElapsedMilliseconds}ms.");

            return permissionToken == null ?
                new BadRequestObjectResult("Unable to create permissioon token for user.") :
                    (ActionResult)new OkObjectResult(permissionToken);

        }

        /// <summary>
        /// Gets the user id based on the authenication that was setup via the azure portal.  Once a user
        /// is logged in, you can navigate to {host}/.auth/me to get authentication information.
        /// </summary>
        /// <param name="host">The host the user authenticated with.  Used to check the auth properties.</param>
        /// <param name="accessToken">AAD or similar access token received from authentication.</param>
        /// <returns></returns>
        private static async Task<string> GetUserIDFromAccessToken(string host, string accessToken)
        {
            string userId = string.Empty;

            http.DefaultRequestHeaders.Add("x-zumo-auth", accessToken);
            var response = await http.GetAsync(host + "/.auth/me");
            string rs = await response.Content.ReadAsStringAsync();
            var rj = JsonConvert.DeserializeObject<JArray>(rs);
            userId = rj.Children().FirstOrDefault().Children<JProperty>().FirstOrDefault(x => x.Name == "user_id").Value.ToString();

            return userId;
        }

        private static async Task<PermissionToken> GetPermissionToken(string userId, CosmosDBRepository repo, PermissionMode permissionMode)
        {
            string permissionDocumentId = $"{userId}_{COLLECTION}Collection_PermissionId";

            PermissionToken permissionToken = null;

            permissionToken = await GetPermissionAsync(userId, repo, permissionDocumentId, PermissionMode.All);

            return permissionToken;
        }

        private static async Task<PermissionToken> GetPermissionAsync(string userId, CosmosDBRepository repo, string permissionId, PermissionMode permissionMode = PermissionMode.Read)
        {
            Permission permission = null;

            User user = await CreateUserIfNotExistAsync(userId, repo);

            try
            {
                permission = await repo.GetPermissionAsync(user, permissionId, new RequestOptions { ResourceTokenExpirySeconds = (int)TOKEN_EXPIRY.TotalSeconds });

                System.Diagnostics.Debug.WriteLine($"Retreived Existing Permission. {permission.Id}");

            }
            catch (DocumentClientException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    DocumentCollection collection = await repo.GetDocumentCollectionAsync();

                    permission = new Permission
                    {

                        PermissionMode = permissionMode,
                        ResourceLink = collection.SelfLink,
                        // Permission restricts access to this partition key
                        ResourcePartitionKey = new PartitionKey(userId),
                        // Unique per user
                        Id = permissionId

                    };

                    try
                    {
                        permission = await repo.UpsertPermissionAsync(user, permission, new RequestOptions { ResourceTokenExpirySeconds = (int)TOKEN_EXPIRY.TotalSeconds });
                    }
                    catch (Exception ex)
                    {

                        throw ex;
                    }

                }
                else throw e;
            }

            var expires = DateTimeOffset.Now.Add(TOKEN_EXPIRY).ToUnixTimeSeconds();

            return new PermissionToken()
            {
                Token = permission.Token,
                Expires = expires,
                UserId = userId
            };
        }

        /// <summary>
        /// Creates a CosmosDB user if the user doesn't already exist.
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="documentClient"></param>
        /// <returns></returns>
        private static async Task<User> CreateUserIfNotExistAsync(string userId, CosmosDBRepository repo)
        {
            User cosmosDBUser = null;

            try
            {
                cosmosDBUser = await repo.GetUserAsync(userId);
            }
            catch (DocumentClientException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    cosmosDBUser = await repo.UpsertUserAsync(new User { Id = userId });
                }
            }

            return cosmosDBUser;
        }

        public static string GetEnvironmentVariable(string name)
        {
            return System.Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        }
    }
}
