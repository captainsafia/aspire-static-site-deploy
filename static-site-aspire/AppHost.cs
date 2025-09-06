#pragma warning disable ASPIREPUBLISHERS001

using Azure.Provisioning.Storage;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Aspire.Hosting.Azure;
using Azure.Storage.Blobs;
using Aspire.Hosting.Publishing;
using System.Diagnostics;
using Azure.Storage.Blobs.Models;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureStorageHosting("deploy");

builder.AddViteApp("static-site", "../static-site")
       .WithNpmPackageInstallation();

builder.Build().Run();

static class DeploymentExtensions
{
    public static IDistributedApplicationBuilder AddAzureStorageHosting(this IDistributedApplicationBuilder builder, [ResourceName] string name)
    {
        if (builder.ExecutionContext.IsRunMode)
        {
            return builder;
        }
        
        var storage = builder.AddAzureStorage($"{name}-storage")
            .ConfigureInfrastructure(infra =>
            {
                var storage = infra.GetProvisionableResources().OfType<StorageAccount>().Single();
                // Needs for static website support
                storage.AllowBlobPublicAccess = true;
            });
        var frontDoor = builder.AddResource(new AzureFrontDoorResource($"{name}-afd"))
            .WithParameter("frontDoorName", $"{name}-afd")
            .WithParameter("storageAccountName", storage.Resource.NameOutputReference);

        builder.AddResource(new AzureStorageHostingResource(name));
        
        return builder;
    }

    class AzureFrontDoorResource(string name): AzureBicepResource(name, templateFile: "front-door.bicep") { }

    class AzureStorageHostingResource : Resource
    {
        public AzureStorageHostingResource(string name) : base(name)
        {
            Annotations.Add(new DeployingCallbackAnnotation(DeployAsync));
        }

        internal async Task DeployAsync(DeployingContext context)
        {
            var storageAccount = context.Model.Resources.OfType<AzureStorageResource>().Single();
            var blobEndpoint = await storageAccount.BlobEndpoint.GetValueAsync();

            if (string.IsNullOrEmpty(blobEndpoint))
            {
                context.Logger.LogError("Failed to get blob endpoint from storage account");
                return;
            }

            // Create the main deployment step
            var deploymentStep = await context.ActivityReporter.CreateStepAsync("Deploying static site", context.CancellationToken).ConfigureAwait(false);
            await using (deploymentStep.ConfigureAwait(false))
            {
                try
                {
                    // Step 1: Build the static site using npm run build
                    if (!await TryBuildStaticSite(deploymentStep, context))
                    {
                        return;
                    }

                    // Step 2: Configure static website on storage account
                    if (!await TryConfigureStaticWebsite(deploymentStep, blobEndpoint, context))
                    {
                        return;
                    }

                    // Step 3: Upload files to storage
                    if (!await TryUploadStaticFiles(deploymentStep, blobEndpoint, context))
                    {
                        return;
                    }

                    await deploymentStep.CompleteAsync("Successfully deployed static site", CompletionState.Completed, context.CancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await deploymentStep.FailAsync($"Static site deployment failed: {ex.Message}", context.CancellationToken).ConfigureAwait(false);
                    throw;
                }
            }

            var frontDoor = context.Model.Resources.OfType<AzureFrontDoorResource>().Single();
            var endpoint = frontDoor.Outputs["endpointUrl"] as string;
            await context.ActivityReporter.CompletePublishAsync($"Static site deployed successfully! Access it at: {endpoint}").ConfigureAwait(false);
        }

        private async Task<bool> TryBuildStaticSite(IPublishingStep parentStep, DeployingContext context)
        {
            var buildTask = await parentStep.CreateTaskAsync("Building static site with npm", context.CancellationToken).ConfigureAwait(false);
            await using (buildTask.ConfigureAwait(false))
            {
                try
                {
                    var staticSitePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "static-site");
                    
                    if (!Directory.Exists(staticSitePath))
                    {
                        await buildTask.FailAsync($"Static site directory not found: {staticSitePath}", context.CancellationToken).ConfigureAwait(false);
                        return false;
                    }

                    context.Logger.LogInformation("Running npm install in {Path}", staticSitePath);

                    var installProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "npm",
                            Arguments = "install",
                            WorkingDirectory = staticSitePath,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };

                    installProcess.Start();
                    await installProcess.WaitForExitAsync(context.CancellationToken);

                    if (installProcess.ExitCode != 0)
                    {
                        var error = await installProcess.StandardError.ReadToEndAsync();
                        await buildTask.FailAsync($"npm install failed with exit code {installProcess.ExitCode}: {error}", context.CancellationToken).ConfigureAwait(false);
                        return false;
                    }

                    context.Logger.LogInformation("Running npm run build in {Path}", staticSitePath);

                    var buildProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "npm",
                            Arguments = "run build",
                            WorkingDirectory = staticSitePath,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };

                    buildProcess.Start();
                    await buildProcess.WaitForExitAsync(context.CancellationToken);

                    if (buildProcess.ExitCode != 0)
                    {
                        var error = await buildProcess.StandardError.ReadToEndAsync();
                        await buildTask.FailAsync($"npm run build failed with exit code {buildProcess.ExitCode}: {error}", context.CancellationToken).ConfigureAwait(false);
                        return false;
                    }

                    await buildTask.CompleteAsync("Successfully built static site", CompletionState.Completed, context.CancellationToken).ConfigureAwait(false);
                    return true;
                }
                catch (Exception ex)
                {
                    await buildTask.FailAsync($"Build failed: {ex.Message}", context.CancellationToken).ConfigureAwait(false);
                    return false;
                }
            }
        }

        private async Task<bool> TryConfigureStaticWebsite(IPublishingStep parentStep, string blobEndpoint, DeployingContext context)
        {
            var configTask = await parentStep.CreateTaskAsync("Configuring static website service", context.CancellationToken).ConfigureAwait(false);
            await using (configTask.ConfigureAwait(false))
            {
                try
                {
                    var blobServiceClient = new BlobServiceClient(new Uri(blobEndpoint), new DefaultAzureCredential());
                    
                    // Get current properties first to avoid null reference exceptions
                    var currentProperties = await blobServiceClient.GetPropertiesAsync(context.CancellationToken);
                    var properties = currentProperties.Value;
                    
                    // Update only the StaticWebsite property
                    properties.StaticWebsite = new BlobStaticWebsite
                    {
                        IndexDocument = "index.html",
                        ErrorDocument404Path = "index.html",
                        Enabled = true
                    };
                    
                    await blobServiceClient.SetPropertiesAsync(properties, context.CancellationToken);

                    await configTask.CompleteAsync("Successfully configured static website service", CompletionState.Completed, context.CancellationToken).ConfigureAwait(false);
                    return true;
                }
                catch (Exception ex)
                {
                    await configTask.FailAsync($"Failed to configure static website: {ex.Message}", context.CancellationToken).ConfigureAwait(false);
                    return false;
                }
            }
        }

        private async Task<bool> TryUploadStaticFiles(IPublishingStep parentStep, string blobEndpoint, DeployingContext context)
        {
            var uploadTask = await parentStep.CreateTaskAsync("Uploading static files to storage", context.CancellationToken).ConfigureAwait(false);
            await using (uploadTask.ConfigureAwait(false))
            {
                try
                {
                    var blobServiceClient = new BlobServiceClient(new Uri(blobEndpoint), new DefaultAzureCredential());
                    var containerClient = blobServiceClient.GetBlobContainerClient("$web");
                    
                    await containerClient.CreateIfNotExistsAsync(cancellationToken: context.CancellationToken);

                    var staticSitePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "static-site");
                    var distPath = Path.Combine(staticSitePath, "dist");
                    
                    if (!Directory.Exists(distPath))
                    {
                        await uploadTask.FailAsync($"Build output directory not found: {distPath}", context.CancellationToken).ConfigureAwait(false);
                        return false;
                    }

                    var files = Directory.GetFiles(distPath, "*", SearchOption.AllDirectories);
                    var uploadTasks = new List<Task>();

                    context.Logger.LogInformation("Uploading {FileCount} files to static website", files.Length);

                    foreach (var file in files)
                    {
                        var relativePath = Path.GetRelativePath(distPath, file);
                        var blobName = relativePath.Replace(Path.DirectorySeparatorChar, '/');
                        
                        var blobClient = containerClient.GetBlobClient(blobName);
                        
                        var contentType = GetContentType(file);
                        var uploadOptions = new BlobUploadOptions
                        {
                            HttpHeaders = new BlobHttpHeaders
                            {
                                ContentType = contentType
                            }
                        };
                        
                        var uploadFileTask = blobClient.UploadAsync(file, uploadOptions, context.CancellationToken);
                        uploadTasks.Add(uploadFileTask);
                    }

                    await Task.WhenAll(uploadTasks);

                    await uploadTask.CompleteAsync($"Successfully uploaded {files.Length} files to static website", CompletionState.Completed, context.CancellationToken).ConfigureAwait(false);
                    return true;
                }
                catch (Exception ex)
                {
                    await uploadTask.FailAsync($"Failed to upload files: {ex.Message}", context.CancellationToken).ConfigureAwait(false);
                    return false;
                }
            }
        }
        
        private static string GetContentType(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".html" => "text/html",
                ".htm" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".mjs" => "application/javascript",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".txt" => "text/plain",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                ".ttf" => "font/ttf",
                ".eot" => "application/vnd.ms-fontobject",
                ".otf" => "font/otf",
                ".pdf" => "application/pdf",
                ".zip" => "application/zip",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".ogg" => "audio/ogg",
                _ => "application/octet-stream"
            };
        }
    }

}