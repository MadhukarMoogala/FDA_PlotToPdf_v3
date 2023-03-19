namespace ClientV3
{
    using Autodesk.Forge;
    using Autodesk.Forge.Client;
    using Autodesk.Forge.Core;
    using Autodesk.Forge.DesignAutomation;
    using Autodesk.Forge.DesignAutomation.Model;
    using Autodesk.Forge.Model;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Defines the <see cref="App" />.
    /// </summary>
    class App
    {
        /// <summary>
        /// Defines the ActivityName.
        /// </summary>
        static string ActivityName = "AutoCAD.PlotToPDF+prod";

        /// <summary>
        /// Defines the PackageName.
        /// </summary>
        static string PackageName = string.Empty;

        //e.g. Owner = MyTestApp (it must be *globally* unique)
        /// <summary>
        /// Defines the Owner.
        /// </summary>
        static readonly string Owner = "dasplottingwork";

        //need to set, singed resource, like OSS, S3.
        /// <summary>
        /// Defines the UploadUrl.
        /// </summary>
        static string UploadUrl = "";

        /// <summary>
        /// Defines the bucketKey.
        /// </summary>
        static string bucketKey = "";

        
        /// <summary>
        /// Gets or sets the InternalToken.
        /// </summary>
        private static dynamic InternalToken { get; set; }

        /// <summary>
        /// Defines the api.
        /// </summary>
        public DesignAutomationClient api;

        /// <summary>
        /// Defines the config.
        /// </summary>
        public ForgeConfiguration config;

        /// <summary>
        /// Initializes a new instance of the <see cref="App"/> class.
        /// </summary>
        /// <param name="api">The api<see cref="DesignAutomationClient"/>.</param>
        /// <param name="config">The config<see cref="IOptions{ForgeConfiguration}"/>.</param>
        public App(DesignAutomationClient api, IOptions<ForgeConfiguration> config)
        {
            this.api = api;
            this.config = config.Value;
        }

        /// <summary>
        /// The GetInternalAsync.
        /// </summary>
        /// <returns>The <see cref="Task{dynamic}"/>.</returns>
        public async Task<dynamic> GetInternalAsync()
        {
            if (InternalToken == null || InternalToken.ExpiresAt < DateTime.UtcNow)
            {
                InternalToken = await Get2LeggedTokenAsync(new Scope[] { Scope.BucketCreate, Scope.BucketRead, Scope.BucketDelete, Scope.DataRead, Scope.DataWrite, Scope.DataCreate, Scope.CodeAll });
                InternalToken.ExpiresAt = DateTime.UtcNow.AddSeconds(InternalToken.expires_in);
            }

            return InternalToken;
        }

        /// <summary>
        /// Get the access token from Autodesk.
        /// </summary>
        /// <param name="scopes">The scopes<see cref="Scope[]"/>.</param>
        /// <returns>The <see cref="Task{dynamic}"/>.</returns>
        public async Task<dynamic> Get2LeggedTokenAsync(Scope[] scopes)
        {
            TwoLeggedApi oauth = new TwoLeggedApi();
            string grantType = "client_credentials";

            Console.WriteLine($"{config.ClientId} \t {config.ClientSecret} ");
            dynamic bearer = await oauth.AuthenticateAsync(config.ClientId,
              config.ClientSecret,
              grantType,
              scopes);
            return bearer;
        }

        /// <summary>
        /// The RunAsync.
        /// </summary>
        /// <returns>The <see cref="Task"/>.</returns>
        public async Task RunAsync()
        {
            if (string.IsNullOrEmpty(Owner))
            {
                Console.WriteLine("Please provide non-empty Owner.");
                return;
            }

            if (string.IsNullOrEmpty(UploadUrl))
            {
                Console.WriteLine($"Creating Bucket....");
                dynamic oauth = await GetInternalAsync();

                // 1. ensure bucket exists
                bucketKey = $"{config.ClientId.ToLower()}_{Guid.NewGuid():N}";
                Console.WriteLine($"Creating Bucket {bucketKey}....");
                BucketsApi buckets = new BucketsApi();
                buckets.Configuration.AccessToken = oauth.access_token;
                try
                {
                    PostBucketsPayload bucketPayload = new PostBucketsPayload(bucketKey, null, PostBucketsPayload.PolicyKeyEnum.Transient);
                    dynamic bucketsRes = await buckets.CreateBucketAsync(bucketPayload, "US");
                }
                catch(Exception ex)
                {
                    // in case bucket already exists
                    Console.WriteLine($"\tBucket {bucketKey} exists ..{ex.StackTrace}");
                };
               

            }

            if (!await SetupOwnerAsync())
            {
                Console.WriteLine("Exiting.");
                return;
            }

            await SubmitWorkItemAsync(ActivityName);
        }

        static void onUploadProgress(float progress, TimeSpan elapsed, List<UploadItemDesc> objects)
        {
            Console.WriteLine("progress: {0} elapsed: {1} objects: {2}", progress, elapsed, string.Join(", ", objects));
        }
        private static async Task<string> GetObjectId(string bucketKey, string objectKey, dynamic oauth, string fileSavePath)
        {
            try
            {
                ObjectsApi objectsApi = new ObjectsApi();
                objectsApi.Configuration.AccessToken = oauth.access_token;
               // var data = await File.ReadAllBytesAsync(fileSavePath);

                using FileStream _stream = File.Open(fileSavePath, FileMode.Open);
                List<UploadItemDesc> uploadRes = await objectsApi.uploadResources(bucketKey,
                       new List<UploadItemDesc> {
                        new UploadItemDesc(objectKey, _stream)
                       },
                       new Dictionary<string, object>()
                       {					
					    { "minutesExpiration", 60 }					   
				       },
                       onUploadProgress
                      );
                Console.WriteLine("**** Upload object(s) response(s):");
                DynamicDictionary objValues = uploadRes[0].completed;
                objValues.Dictionary.TryGetValue("objectId", out var id);

                return id?.ToString();

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception when preparing input url:{ex.Message}");
                throw;
            }

        }

        /// <summary>
        /// The SubmitWorkItemAsync.
        /// </summary>
        /// <param name="myActivity">The myActivity<see cref="string"/>.</param>
        /// <returns>The <see cref="Task"/>.</returns>
        private async Task SubmitWorkItemAsync(string myActivity)
        {
            Console.WriteLine("Submitting up WorkItem...");
            dynamic oauth = await GetInternalAsync();
            string outputFileOss= string.Format("{0}_output_{1}", DateTime.Now.ToString("yyyyMMddhhmmss"), "result.pdf");
            string inputFileOss = string.Format("{0}_input_{1}", DateTime.Now.ToString("yyyyMMddhhmmss"), Path.GetFileName(FilePaths.InputFile));
            var workItemStatus = await api.CreateWorkItemAsync(new WorkItem()
            {
                ActivityId = myActivity,
                Arguments = new Dictionary<string, IArgument>()
                {
                    { "HostDwg",
                        new XrefTreeArgument()
                        {
                        Verb=Verb.Get,
                        Url = await GetObjectId(bucketKey,inputFileOss,oauth,Path.GetFullPath(FilePaths.InputFile)),
                        Headers = new Dictionary<string, string>()
                              {
                                 { "Authorization", "Bearer " + oauth.access_token }
                              }
                        }
                    },
                    { "Result",
                        new XrefTreeArgument()
                        {
                              Verb=Verb.Put,
                              Url =  await GetObjectId(bucketKey,outputFileOss,oauth,Path.GetFullPath(FilePaths.InputFile)),
                              Headers = new Dictionary<string, string>()
                              {
                                 { "Authorization", "Bearer " + oauth.access_token }
                              }
                    }
                    }
                }
            });

            Console.Write("\tPolling status");
            while (!workItemStatus.Status.IsDone())
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                workItemStatus = await api.GetWorkitemStatusAsync(workItemStatus.Id);
                Console.Write(".");
            }
            Console.WriteLine($"{workItemStatus.Status}.");
            var fname = await DownloadToDocsAsync(workItemStatus.ReportUrl, "Das-report.txt");
            Console.WriteLine($"Downloaded {fname}.");            
            var result = await DownloadToDocsAsync(UploadUrl, outputFileOss, true);
            Console.WriteLine($"Downloaded {result}.");
        }

        /// <summary>
        /// The SetupOwnerAsync.
        /// </summary>
        /// <returns>The <see cref="Task{bool}"/>.</returns>
        private async Task<bool> SetupOwnerAsync()
        {
            Console.WriteLine("Setting up owner...");
            var nickname = await api.GetNicknameAsync("me");
            if (nickname == config.ClientId)
            {
                Console.WriteLine("\tNo nickname for this clientId yet. Attempting to create one...");
                HttpResponseMessage resp;
                resp = await api.ForgeAppsApi.CreateNicknameAsync("me", new NicknameRecord() { Nickname = Owner }, throwOnError: false);
                if (resp.StatusCode == HttpStatusCode.Conflict)
                {
                    Console.WriteLine("\tThere are already resources associated with this clientId or nickname is in use. Please use a different clientId or nickname.");
                    return false;
                }
                resp.EnsureSuccessStatusCode();
            }
            return true;
        }



        /// <summary>
        /// The DownloadToDocsAsync.
        /// </summary>
        /// <param name="url">The url<see cref="string"/>.</param>
        /// <param name="localFile">The localFile<see cref="string"/>.</param>
        /// <returns>The <see cref="Task{string}"/>.</returns>
        async Task<string> DownloadToDocsAsync(string url, string localFile, bool isOauthRequired = false)
        {
          
            var report = FilePaths.OutPut;
            var fname = Path.Combine(report, localFile);
            if (File.Exists(fname))
                File.Delete(fname);            
            using var client = new HttpClient();
            if (isOauthRequired)
            {
                dynamic oAuth = await GetInternalAsync();
                ObjectsApi objectsApi = new ObjectsApi();
                objectsApi.Configuration.AccessToken = oAuth.access_token;

                Autodesk.Forge.Client.ApiResponse<dynamic> res = await objectsApi.getS3DownloadURLAsyncWithHttpInfo(
                                        bucketKey,
                                        localFile,
                                        new Dictionary<string, object>
                                        {
                                            { "minutesExpiration", 15.0 },
                                            { "useCdn", true }
                                        });
                url = res.Data.url;
                
            }

            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            using (var fs = new FileStream(fname, FileMode.CreateNew))
            {
                await response.Content.CopyToAsync(fs);
            }

            return fname;
        }
    }
}
