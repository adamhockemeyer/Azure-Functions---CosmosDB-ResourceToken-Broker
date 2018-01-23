# Using Azure Functions as a Resource Token Broker for Cosmos DB

Welcome to the repo!

Head right over to the [Wiki](https://github.com/adamhockemeyer/Azure-Functions---CosmosDB-ResourceToken-Broker/wiki) for a guide to get started!

[1 - Create Cosmos DB Account](https://github.com/adamhockemeyer/Azure-Functions---CosmosDB-ResourceToken-Broker/wiki/1-Create-Cosmos-DB-Account)

[2 - Create an Azure Function App with Authentication](https://github.com/adamhockemeyer/Azure-Functions---CosmosDB-ResourceToken-Broker/wiki/2-Create-an-Azure-Function-App-with-Authentication-Authorization)

[3 - Test with Postman](https://github.com/adamhockemeyer/Azure-Functions---CosmosDB-ResourceToken-Broker/wiki/3-Testing-with-Postman)

[4 - Cosmos DB Repository Class](https://github.com/adamhockemeyer/Azure-Functions---CosmosDB-ResourceToken-Broker/wiki/4-Cosmos-DB-Repository-Class)

[5- Cosmos DB Users, Permissions, Partition Keys](https://github.com/adamhockemeyer/Azure-Functions---CosmosDB-ResourceToken-Broker/wiki/5-Cosmos-DB-Users,-Permissions,-Partition-Keys)

[6-View Cosmos DB SQL Query](https://github.com/adamhockemeyer/Azure-Functions---CosmosDB-ResourceToken-Broker/wiki/6-View-Cosmos-DB-SQL-Query)

### Introduction

***

If you want to call Cosmos DB directly from a client application without giving your client the your Cosmos master key, this is an example for you.

This repo uses the following technologies to enable a client application to be able to safely call Cosmos DB directly based on permissions that have been granted to them:
* [Azure Function App](https://docs.microsoft.com/en-us/azure/azure-functions/functions-overview)
* [App Service Authentication\Authorization](https://docs.microsoft.com/en-us/azure/app-service/app-service-authentication-overview)
* [Cosmos DB](https://docs.microsoft.com/en-us/azure/cosmos-db/introduction)

The repo has two main objectives:
* Create a lightweight service that can return a token to Cosmos DB for the client to make calls to Cosmos DB directly
* Create a lightweight C# Cosmos DB repository for easily interacting with the Cosmos DB client sdk.

### Motivation

***

Cosmos DB provides an easy and distributed way to store documents (example: JSON serialized class data) and attachments that can be geo-replicated across the globe with minimal work on your end.  Your data can scale automatically and you can configure Cosmos DB to handle huge throughput of data and requests.

Cosmos DB can be called via REST or by various client libraries depending on your preferred development language.

Two cool things are that Cosmos DB can easily replicate to different regions around the globe, and that Cosmos DB can be called directly from a client application which (nearly) reduces the need for the traditional middle-tier web service layer (if your applications allows for it). 

Wouldn't it be nice to:
* Quickly get your project developed with minimal "backend" work
* Let the client application be able to call the database directly to eliminate the need to scale the middle-tier application
* Let the client be able to read data from the replication closest to them

This repo focuses mainly on the first two items, the the last item is now very easy with just a configuration.

Cosmos DB will give you a "master" key by default.  This key has access to do any action to your account such as create/delete databases, collections, users, permissions, etc.  You can imagine that you **would not want to give this key or embed it into your client applications**.  If a smart user was able to get ahold of this key from your application, your entire database could be manipulated by them.  The master key would be fine to use in a traditional mid-tier application where for example a .net Web API application has the key and your users call a web service and then your web service interacts with Cosmos DB and returns the result to the client.  In that case your client isn't calling Cosmos DB directly.  However, the scenarios presented in this repo is to create a resource key\token that we can give to the client to be able to then call directly to cosmos db, without potentially compromising the database.

### Objective 1
We will create an Azure Function to use the master key of our Cosmos DB account to create a User in Cosmos DB, create some permissions for the user, and then return a [Resource Token](https://docs.microsoft.com/en-us/azure/cosmos-db/secure-access-to-data) which allows the user to call Cosmos DB directly to interact with the database and collections so long as the permission allows for the action.  An Azure Function is perfect for this use case because we only don't want to give the client access to the master key, so we can create a function which secures the master key and give the client their specific token to call the database with.  Also since an Azure Function can be setup on a consumption plan, we are only paying for this simple service whenever a client needs to refresh their resource token after it expires (5 hours max).

### Objective 2
The second objective was to create a simple repository wrapper on top of the .Net client SDK which would save some duplicated code and provide some additional support for semi-structured data in terms of serialized C# POCO objects and being able to query on the type of documents in the database in a unified way.  We can use the resource token the client received from objective 1, and set this in our Cosmos DB repository to use for requests to Cosmos DB.

