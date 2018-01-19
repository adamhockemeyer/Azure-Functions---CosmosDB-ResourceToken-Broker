using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;

using CosmosDBResourceTokenBroker.Shared.Interface;



namespace CosmosDBResourceTokenBroker.Shared
{
    public class CosmosDBRepository : IDataRepository, IDataStoreAuthRepository
    {
        private string _database = string.Empty;
        private string _collection = string.Empty;
        private string _cosmosEndpoint = string.Empty;
        private string _authKeyOrResourceToken = string.Empty;
        private string _partitionKey = string.Empty;

        private static readonly Lazy<CosmosDBRepository> _instance = new Lazy<CosmosDBRepository>(() => new CosmosDBRepository());

        public static CosmosDBRepository Instance => _instance.Value;

        private Uri DocumentCollectionUri => UriFactory.CreateDocumentCollectionUri(_database, _collection);

        private DocumentClient documentClient = null;

        private CosmosDBRepository()
        {
            // Additional Initialization.
        }

        /// <summary>
        /// Creates the document client based on the current config values that have been assigned by the Fluent API.
        /// </summary>
        private void TryCreateDocumentClient()
        {
            if(!string.IsNullOrEmpty(_cosmosEndpoint) && !string.IsNullOrEmpty(_authKeyOrResourceToken))
            {
                documentClient = new DocumentClient(
                    new Uri(_cosmosEndpoint), 
                    _authKeyOrResourceToken, 
                    new ConnectionPolicy
                    {
                        RetryOptions = new RetryOptions
                        {
                            MaxRetryAttemptsOnThrottledRequests = 3,
                            MaxRetryWaitTimeInSeconds = 15
                        },
                        ConnectionMode = ConnectionMode.Direct,     // Use Direct if possible for better performance
                        ConnectionProtocol = Protocol.Tcp           // Use TCP if possible for better performance
                    }
                    );

                System.Diagnostics.Debug.WriteLine($"Creating DocumentClient...");
            }
        }

        private void TrySetPartitionKey(ref FeedOptions options)
        {
            if(!string.IsNullOrEmpty(_partitionKey))
            {
                if(options == null)
                {
                    options = new FeedOptions { PartitionKey = new PartitionKey(_partitionKey) };
                }
                else
                {
                    options.PartitionKey = new PartitionKey(_partitionKey);
                } 
            }
        }

        private void TrySetPartitionKey(ref RequestOptions options)
        {
            if (!string.IsNullOrEmpty(_partitionKey))
            {
                if (options == null)
                {
                    options = new RequestOptions { PartitionKey = new PartitionKey(_partitionKey) };
                }
                else
                {
                    options.PartitionKey = new PartitionKey(_partitionKey);
                }
            }
        }

        private void TrySetPartitionKey<T>(ref T typedDocument) where T : TypedDocument<T>
        {
            if(!string.IsNullOrEmpty(_partitionKey) && typedDocument != null)
            {
                typedDocument.PartitionKey = typedDocument.PartitionKey ?? _partitionKey; 
            }
        }

        #region Fluent API

        /// <summary>
        /// Parses a connection string into endpoint and auth key/token values.
        /// CosmosDB Connection String found in the Azure Portal under "Keys".
        /// If using a read-write key, this repository should only be used behind a middle-tier
        /// service so the senstive key can kept confidential.
        /// </summary>
        /// <param name="connectionString">In the format "AccountEndpoint=https://{cosmosdb-resource-name}.documents.azure.com:443/;AccountKey={Key-or-ResourceToken}.</param>
        /// <returns></returns>
        public CosmosDBRepository ConnectionString(string connectionString)
        {
            const string ACCOUNT_ENDPOINT = "AccountEndpoint=";
            const string ACCOUNT_KEY = "AccountKey=";

            var components = connectionString.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            if(components == null || components.Length != 2 || (!connectionString.Contains(ACCOUNT_KEY) || !connectionString.Contains(ACCOUNT_ENDPOINT) ))
            {
                throw new Exception("The connection string must contain \"AccountEndpoint=\" and \"AccountKey=\" seperated by a semi-colon");
            }
         
            if(components[0].Contains(ACCOUNT_ENDPOINT))
            {
                _cosmosEndpoint = components[0].Replace(ACCOUNT_ENDPOINT, "");
                _authKeyOrResourceToken = components[1].Replace(ACCOUNT_KEY, "");
            }
            else
            {
                _cosmosEndpoint = components[1].Replace(ACCOUNT_ENDPOINT, "");
                _authKeyOrResourceToken = components[0].Replace(ACCOUNT_KEY, "");
            }

            TryCreateDocumentClient();

            return this;
        }

        /// <summary>
        /// Sets the endpoint of the CosmosDB resource.  You can set either a connection string or an <see cref="Endpoint(string)"/> and <see cref="AuthKeyOrResourceToken(string)"/>.
        /// </summary>
        /// <param name="endpoint">I.E. https://{cosmosdb-resource-name}.documents.azure.com:443/</param>
        /// <returns></returns>
        public CosmosDBRepository Endpoint(string endpoint)
        {
            if(!_cosmosEndpoint.Equals(endpoint, StringComparison.OrdinalIgnoreCase))
            {
                _cosmosEndpoint = endpoint;
                TryCreateDocumentClient();
            }
            
            return this;
        }

        /// <summary>
        /// Auth Key can be found in the Azure Portal under "Keys" or a ResourceToken can be used for access by user permission.
        /// </summary>
        /// <param name="authKeyOrResourceToken">Auth key should only be used when the key is read-only, 
        /// otherwise a read-write auth keys (master) should only be used behind a middle-tier service and not stored on the end client!
        /// ResourceTokens can be used on the end client.</param>
        /// <returns></returns>
        public CosmosDBRepository AuthKeyOrResourceToken(string authKeyOrResourceToken)
        {
            if(!_authKeyOrResourceToken.Equals(authKeyOrResourceToken,StringComparison.OrdinalIgnoreCase))
            {
                _authKeyOrResourceToken = authKeyOrResourceToken;
                TryCreateDocumentClient();
            }
            
            return this;
        }

        public CosmosDBRepository Database(string databaseName)
        {
            _database = databaseName;
            return this;
        }

        public CosmosDBRepository Collection(string collectionName)
        {
            _collection = collectionName;
            return this;
        }

        public CosmosDBRepository PartitionKey(string partitionKey)
        {
            _partitionKey = partitionKey;
            return this;
        }

        #endregion

        #region Data Access

        public async Task<T> GetItemAsync<T>(Expression<Func<T, bool>> predicate, FeedOptions feedOptions = null) where T : TypedDocument<T>
        {
            TrySetPartitionKey(ref feedOptions);

            // Add the 'Type' of the document as a query filter, so documents can be filtered by a specific type.
            Expression<Func<T, bool>> typeCheck = p => p.Type == typeof(T).Name;

            IDocumentQuery<T> query = documentClient.CreateDocumentQuery<T>(DocumentCollectionUri, feedOptions)
                .Where(typeCheck)
                .Where(predicate)
                .AsDocumentQuery();

            var results = await query.ExecuteNextAsync<T>();

            return results?.FirstOrDefault();
        }

        public async Task<T> GetItemAsync<T>(string documentId, RequestOptions requestOptions = null) where T : TypedDocument<T>
        {
            TrySetPartitionKey(ref requestOptions);

            return await documentClient.ReadDocumentAsync<T>(UriFactory.CreateDocumentUri(_database, _collection, documentId), requestOptions);
        }

        public async Task<IEnumerable<T>> GetItemsAsync<T>(Expression<Func<T, bool>> predicate, FeedOptions feedOptions = null) where T : TypedDocument<T>
        {
            TrySetPartitionKey(ref feedOptions);

            var results = new List<T>();

            // Add the 'Type' of the document as a query filter, so documents can be filtered by a specific type.
            Expression<Func<T, bool>> typeCheck = p => p.Type == typeof(T).Name;

            IDocumentQuery<T> query = documentClient.CreateDocumentQuery<T>(DocumentCollectionUri, feedOptions)
                .Where(typeCheck)
                .Where(predicate)
                .AsDocumentQuery();

            while(query.HasMoreResults)
            {
                var documents = await query.ExecuteNextAsync<T>();
                
                results.AddRange(documents);
            }

            return results.AsEnumerable();
        }

        public async Task<IEnumerable<T>> GetAllItemsAsync<T>(FeedOptions feedOptions = null) where T : TypedDocument<T>
        {
            TrySetPartitionKey(ref feedOptions);

            var results = new List<T>();

            // Add the 'Type' of the document as a query filter, so documents can be filtered by a specific type.
            Expression<Func<T, bool>> typeCheck = p => p.Type == typeof(T).Name;

            IDocumentQuery<T> query = documentClient.CreateDocumentQuery<T>(DocumentCollectionUri, feedOptions)
                .Where(typeCheck)
                .AsDocumentQuery();

            while (query.HasMoreResults)
            {
                var documents = await query.ExecuteNextAsync<T>();

                results.AddRange(documents);
            }

            return results.AsEnumerable();
        }


        public async Task<bool> RemoveItemAsync<T>(string documentId, RequestOptions requestOptions = null) where T : TypedDocument<T>
        {
            TrySetPartitionKey(ref requestOptions);

            try
            {
                await documentClient.DeleteDocumentAsync(UriFactory.CreateDocumentUri(_database, _collection, documentId), requestOptions);
                return true;
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"Unable to delete document. {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Removes a document from the collection.
        /// Convenience method to pass the whole document.  
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="document"></param>
        /// <param name="requestOptions"></param>
        /// <returns>True or False if the document was removed.</returns>
        public async Task<bool> RemoveItemAsync<T>(T document, RequestOptions requestOptions = null) where T : TypedDocument<T>
        {
            TrySetPartitionKey(ref requestOptions);

            return await RemoveItemAsync<T>(document.Id, requestOptions);
        }

        public async Task<bool> RemoveItemsAsync<T>(IEnumerable<T> documents, RequestOptions requestOptions = null) where T : TypedDocument<T>
        {
            TrySetPartitionKey(ref requestOptions);

            // TODO: Change to sproc.
            // Check for existing bulk delete sproc, if not exist, create, and execute against 'documents' property.

            // Hack for now.
            try
            {
                await Task.Run(() =>
                {
                    Parallel.ForEach(documents, (d) => { bool result = RemoveItemAsync(d, requestOptions).Result; });
                });

                return true;
            }
            catch (Exception e)
            {
                throw e;
            }       
        }

        public async Task<T> UpsertItemAsync<T>(T document, RequestOptions requestOptions = null) where T : TypedDocument<T>
        {
            TrySetPartitionKey(ref document);

            var response = await documentClient.UpsertDocumentAsync(DocumentCollectionUri, document, requestOptions);

            return (dynamic)response.Resource;
        }


        #endregion

        #region User/Permission Access

        /// <summary>
        /// Get a permission for a user.
        /// <para/>Note: Should only be called from a mid-tier service.
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="permissionId"></param>
        /// <returns>A CosmosDB <see cref="Permission"/></returns>
        public async Task<Permission> GetPermissionAsync(User user, string permissionId, RequestOptions requestOptions = null)
        {
            var feed = await documentClient.ReadPermissionFeedAsync(user.PermissionsLink);

           return await documentClient.ReadPermissionAsync(UriFactory.CreatePermissionUri(_database,user.Id, permissionId), requestOptions);
        }

        /// <summary>
        /// Creates or Updates a permission for a user.
        /// <para/>Note: Should only be called from a mid-tier service.
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="permission"></param>
        /// <param name="requestOptions"></param>
        /// <returns>A CosmosDB <see cref="Permission"/></returns>
        public async Task<Permission> UpsertPermissionAsync(User user, Permission permission, RequestOptions requestOptions = null)
        {
            return await documentClient.UpsertPermissionAsync(user.SelfLink, permission, requestOptions );
        }

        /// <summary>
        /// Removes a permission
        /// <para/>Note: Should only be called from a mid-tier service.
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="permissionId"></param>
        /// <param name="requestOptions"></param>
        /// <returns></returns>
        public async Task<bool> RemovePermissionAsync(string userId, string permissionId, RequestOptions requestOptions = null)
        {
            try
            {
                await documentClient.DeletePermissionAsync(UriFactory.CreatePermissionUri(_database, userId, permissionId), requestOptions);
                return true;
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"Unable to remove permission. {e.Message}");
                return false;
            }
            
        }

        /// <summary>
        /// Get a user.
        /// <para/>Note: Should only be called from a mid-tier service.
        /// </summary>
        /// <param name="userId"></param>
        /// <returns>A CosmosDB <see cref="User"/>,</returns>
        public async Task<User> GetUserAsync(string userId)
        {
            return await documentClient.ReadUserAsync(UriFactory.CreateUserUri(_database, userId));
        }

        /// <summary>
        /// Creates or Updates a <see cref="User"/> in the database.
        /// <para/>Note: Should only be called from a mid-tier service.
        /// </summary>
        /// <param name="user"></param>
        /// <returns>A CosmosDB <see cref="User"/></returns>
        public async Task<User> UpsertUserAsync(User user)
        {
            return await documentClient.UpsertUserAsync(UriFactory.CreateDatabaseUri(_database), user);
        }

        /// <summary>
        /// Gets a <see cref="DocumentCollection"/> base on the current database and collection set from <see cref="Database(string)"/> and <see cref="Collection(string)"/>.
        /// </summary>
        /// <returns>A CosmosDB <see cref="DocumentCollection"/></returns>
        public async Task<DocumentCollection> GetDocumentCollectionAsync()
        {
            return await documentClient.ReadDocumentCollectionAsync(UriFactory.CreateDocumentCollectionUri(_database, _collection));
        }

        #endregion
    }
}
