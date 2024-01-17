// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Samples.Common;
using Azure.ResourceManager.Sql;
using Azure.ResourceManager.Sql.Models;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Storage.Models;
using Azure.Storage.Blobs;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ManageWebAppStorageAccountConnection
{
    public class Program
    {
        private const string SUFFIX = ".azurewebsites.net";

        /**
         * Azure App Service basic sample for managing web apps.
         *  - Create a storage account and upload a couple blobs
         *  - Create a web app that contains the connection string to the storage account
         *  - Deploy a Tomcat application that reads from the storage account
         *  - Clean up
         */
        public static async Task RunSample(ArmClient client)
        {
            AzureLocation region = AzureLocation.EastUS;
            string app1Name = Utilities.CreateRandomName("webapp1-");
            string app1Url = app1Name + SUFFIX;
            string storageName = Utilities.CreateRandomName("jsdkstore");
            string containerName = Utilities.CreateRandomName("jcontainer");
            string resourceGroupName = Utilities.CreateRandomName("rg1NEMV_");
            var lro = await client.GetDefaultSubscription().GetResourceGroups().CreateOrUpdateAsync(Azure.WaitUntil.Completed, resourceGroupName, new ResourceGroupData(AzureLocation.EastUS));
            var resourceGroup = lro.Value;

            try
            {
                //============================================================
                // Create a storage account for the web app to use

                Utilities.Log("Creating storage account " + storageName + "...");

                var accountCollection = resourceGroup.GetStorageAccounts();
                var accountData = new StorageAccountCreateOrUpdateContent(new StorageSku("sku"), StorageKind.Storage, region);
                var account_lro = await  accountCollection.CreateOrUpdateAsync(WaitUntil.Completed, storageName, accountData);
                var account = account_lro.Value;

                var accountKey = account.GetKeys().FirstOrDefault().Value;

                var connectionString = $"DefaultEndpointsProtocol=https;AccountName={account.Data.Name};AccountKey={accountKey}";
                var blobClient = new BlobContainerClient(connectionString, containerName);

                Utilities.Log("Created storage account " + account.Data.Name);

                //============================================================
                // Upload a few files to the storage account blobs

                Utilities.Log("Uploading 2 blobs to container " + containerName + "...");
                
                await Utilities.UploadFromFileAsync(
                    blobClient,
                    new[] 
                    {
                        Path.Combine(Utilities.ProjectPath, "Asset", "helloworld.war"),
                        Path.Combine(Utilities.ProjectPath, "Asset", "install_apache.Sh")
                    });

                Utilities.Log("Uploaded 2 blobs to container " + containerName);

                //============================================================
                // Create a web app with a new app service plan

                Utilities.Log("Creating web app " + app1Name + "...");

                var webSiteCollection = resourceGroup.GetWebSites();
                var webSiteData = new WebSiteData(region)
                {
                    SiteConfig = new SiteConfigProperties()
                    {
                        WindowsFxVersion = "PricingTier.StandardS1",
                        NetFrameworkVersion = "NetFrameworkVersion.V4_6",
                        PhpVersion = "PhpVersion.V5_6",
                    },

                };
                var webSite_lro = await webSiteCollection.CreateOrUpdateAsync(Azure.WaitUntil.Completed, app1Name, webSiteData);
                var webSite = webSite_lro.Value;

                Utilities.Log("Created web app " + webSite.Data.Name);
                Utilities.Print(webSite);

                //============================================================
                // Deploy a web app that connects to the storage account
                // Source code: https://github.Com/jianghaolu/azure-samples-blob-explorer

                Utilities.Log("Deploying azure-samples-blob-traverser.war to " + app1Name + " through FTP...");

                var csm = new CsmPublishingProfile()
                {
                    Format = PublishingProfileFormat.Ftp
                };
                Utilities.UploadFileToWebApp(
                    await webSite.GetPublishingProfileXmlWithSecretsAsync(csm),
                    Path.Combine(Utilities.ProjectPath, "Asset", "azure-samples-blob-traverser.war"));

                Utilities.Log("Deployment azure-samples-blob-traverser.war to web app " + webSite.Data.Name + " completed");
                Utilities.Print(webSite);

                // warm up
                Utilities.Log("Warming up " + app1Url + "/azure-samples-blob-traverser...");
                Utilities.CheckAddress("http://" + app1Url + "/azure-samples-blob-traverser");
                Thread.Sleep(5000);
                Utilities.Log("CURLing " + app1Url + "/azure-samples-blob-traverser...");
                Utilities.Log(Utilities.CheckAddress("http://" + app1Url + "/azure-samples-blob-traverser"));
            }
            finally
            {
                try
                {
                    Utilities.Log("Deleting Resource Group: " + resourceGroupName);
                    await resourceGroup.DeleteAsync(WaitUntil.Completed);
                    Utilities.Log("Deleted Resource Group: " + resourceGroupName);
                }
                catch (NullReferenceException)
                {
                    Utilities.Log("Did not create any resources in Azure. No clean up is necessary");
                }
                catch (Exception g)
                {
                    Utilities.Log(g);
                }
            }
        }

        public static async Task Main(string[] args)
        {
            try
            {
                //=================================================================
                // Authenticate
                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
                var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
                var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
                ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                ArmClient client = new ArmClient(credential, subscription);

                // Print selected subscription
                Utilities.Log("Selected subscription: " + client.GetSubscriptions().Id);

                await RunSample(client);

            }
            catch (Exception e)
            {
                Utilities.Log(e);
            }
        }
    }
}