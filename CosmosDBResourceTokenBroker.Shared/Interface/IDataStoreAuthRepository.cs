using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;

namespace CosmosDBResourceTokenBroker.Shared.Interface
{
    public interface IDataStoreAuthRepository
    {
        Task<User> GetUserAsync(string userId);

        Task<User> UpsertUserAsync(User user);

        Task<Permission> GetPermissionAsync(User user, string permissionId, RequestOptions requestOptions = null);

        Task<Permission> UpsertPermissionAsync(User user, Permission permission, RequestOptions requestOptions = null);

        Task<bool> RemovePermissionAsync(string userId, string permissionId, RequestOptions requestOptions = null);

        Task<DocumentCollection> GetDocumentCollectionAsync();
    }
}
