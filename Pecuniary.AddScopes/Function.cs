using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.APIGateway;
using Amazon.APIGateway.Model;
using Amazon.Lambda.Core;
using EricBach.LambdaLogger;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace Pecuniary.AddScopes
{
    public class AddScopesRequest
    {
        public string ApiGatewayBaseUrl { get; set; }
    }
    
    public class Function
    {
        public async Task FunctionHandler(AddScopesRequest request, ILambdaContext context)
        {
            var apiId = request.ApiGatewayBaseUrl;

            Logger.Log($"Going to apply OAuth scopes to the API {apiId}");

            var client = new AmazonAPIGatewayClient();
            var resourcesResponse = await client.GetResourcesAsync(new GetResourcesRequest {
                RestApiId = apiId,
                Limit = 500
            });

            var resources = resourcesResponse.Items;

            Logger.Log($"{resources.Count} resources found");

            var methods = resources.SelectMany(r => r.ResourceMethods.Values).ToList();
            var methodKeys = resources.SelectMany(r => r.ResourceMethods.Keys).ToList();

            foreach (var resource in resources.OrderBy(r => r.Path))
            {
                Logger.Log($"Resource {resource.Path}");

                foreach (var method in resource.ResourceMethods)
                {
                    await ApplyOAuthScopesAsync(client, resource.Id, apiId, method.Key);
                }
            }

            Logger.Log($"Creating deployment to the API {apiId}");

            await client.CreateDeploymentAsync(new CreateDeploymentRequest
            {
                RestApiId = apiId,
                StageName = "Prod",
                Description = $"Dev deployment at {DateTime.UtcNow} UTC"
            });
        }

        private async Task ApplyOAuthScopesAsync(AmazonAPIGatewayClient client, string resourceId, string apiId, string httpMethod)
        {
            var operationList = new List<PatchOperation> {
                new PatchOperation
                {
                    Op = Op.Add,
                    Path = "/authorizationScopes",
                    Value = "pecuniary/read"
                }
            };

            if (httpMethod == "POST" || httpMethod == "PUT")
            {
                operationList.Add(new PatchOperation
                {
                    Op = Op.Add,
                    Path = "/authorizationScopes",
                    Value = "pecuniary/write"
                });
            }

            if (httpMethod != "OPTIONS")
            {
                Logger.Log($"- Applying OAuth scopes to {httpMethod}");

                await client.UpdateMethodAsync(new UpdateMethodRequest
                {
                    HttpMethod = httpMethod,
                    ResourceId = resourceId,
                    RestApiId = apiId,
                    PatchOperations = operationList
                });
            }
        }
    }
}
