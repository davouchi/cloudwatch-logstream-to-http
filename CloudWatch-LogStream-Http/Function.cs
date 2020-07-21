using Amazon.Lambda.Core;
using Newtonsoft.Json;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializerAttribute(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace CloudWatch_LogStream_Http
{
    public class Function
    {
        private readonly static HttpClient client = new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(100)
        })
        {
            Timeout = TimeSpan.FromSeconds(100)
        };

        //private static MemoryMappedFile CreateFile(string content, string filePath)
        //{
        //    var file = MemoryMappedFile.CreateNew(filePath, 4066000);

        //    using (var stream = file.CreateViewStream())
        //    {
        //        using (var writer = new StreamWriter(stream))
        //        {
        //            writer.WriteLine(content);
        //        }
        //    }

        //    return file;
        //}

        private static void CopyTo(Stream src, Stream dest)
        {
            byte[] bytes = new byte[4096];

            int cnt;

            while ((cnt = src.Read(bytes, 0, bytes.Length)) != 0)
            {
                dest.Write(bytes, 0, cnt);
            }
        }

        private static string Unzip(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(msi, CompressionMode.Decompress))
                {
                    CopyTo(gs, mso);
                }

                return Encoding.UTF8.GetString(mso.ToArray());
            }
        }

        private static string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = Convert.FromBase64String(base64EncodedData);

            var decodedData = Unzip(base64EncodedBytes);
            return decodedData;
        }

        public static async Task FunctionHandler(AwsRequestObject logs)
        {
            var response = "System Error Occurred Trying To Process CloudWatchLogStreamToHTTP. Check Inner Exception.";
            int retryCount = 0;
            try
            {
                Console.WriteLine("Log data - " + logs.awslogs.data + " " + " Environment Variables - " + JsonConvert.SerializeObject(Environment.GetEnvironmentVariables()));

                string target = Base64Decode(logs.awslogs.data);

                if (target != null)
                {
                    var decodeObj = JsonConvert.DeserializeObject<AwsDecodedRequestObject>(target);

                    if (Environment.GetEnvironmentVariable("postType") == "single")
                    {
                        foreach (var singleEvent in decodeObj.logEvents)
                        {
                            try
                            {
                                if (singleEvent.message.Contains(Environment.GetEnvironmentVariable("timeFormat")))
                                {
                                    await Task.Run(() => SendLogEvents(singleEvent.message, retryCount));
                                }

                                response = "";
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex);
                                continue;
                            }
                        }
                    }
                    else if (Environment.GetEnvironmentVariable("postType") == "multi" || Environment.GetEnvironmentVariable("postType") == "fileUpload")
                    {
                        try
                        {
                            var messageList = decodeObj.logEvents.Where(x => x.message.Contains(Environment.GetEnvironmentVariable("timeFormat"))).Select(x => x.message).ToList();

                            var payload = new JsonSerializerSettings { Converters = { new SerializerFormatOverload("\n", "") } };

                            var payloadSetting = JsonConvert.SerializeObject(messageList, Formatting.None, payload);

                            var payloadFormat = payloadSetting.Replace("\"{", "{").Replace("}\"", "}");

                            var payloadRegex = Regex.Replace(payloadFormat, @"\\", "");

                            if (Environment.GetEnvironmentVariable("postType") == "multi")
                            {
                                await Task.Run(() => SendLogEvents(payloadRegex, retryCount));
                            }
                            else if (Environment.GetEnvironmentVariable("postType") == "fileUpload")
                            {
                                string fileName = Guid.NewGuid().ToString() + ".txt";

                                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + fileName);

                                //var create = CreateFile(payloadRegex, logPath);
                                Console.WriteLine("Working directory - " + logPath);
                                using (var writer = File.CreateText(logPath))
                                {
                                    await writer.WriteLineAsync(payloadRegex);
                                }

                                await SendFileToEndPoint(logPath, payloadRegex);
                                try
                                {
                                    File.Delete(logPath);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("Error Occurred Trying To Delete - " + logPath + " " + " Exception - " + ex);
                                }
                            }

                            response = "";
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                        }
                    }
                    else
                    {
                        Console.WriteLine("Environment Variable Postype Does Not Exist In Function");
                    }
                }
                else
                {
                    Console.WriteLine("Error occurred trying to decode log file");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Response - " + response + " Exception Thrown - " + ex + " awsTrigger - " + logs.awslogs.data);
            }
        }

        private static async Task SendLogEvents(string logEvent, int retryCount)
        {
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var content = new StringContent(logEvent, Encoding.UTF8, "application/json");

            await Task.Run(() => SendToEndPoint(Environment.GetEnvironmentVariable("uploadDomain"), content, logEvent, retryCount));
        }

        //Send Json File
        private static async Task SendToEndPoint(string url, StringContent content, string logEvent, int retryCount)
        {
            retryCount++;

            try
            {
                HttpResponseMessage response = await client.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    Console.WriteLine("Error Occurred Processing event - " + logEvent + " HTTP Status Code - " + response.StatusCode);
                }
                else
                {
                    Console.WriteLine("Log event was sent to endpoint with status code - " + response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                if (retryCount != Convert.ToInt32(Environment.GetEnvironmentVariable("retryCount")))
                {
                    Console.WriteLine("Retrying Log Event Post To Endpoint. Retry Count - " + retryCount);
                    await Task.Run(() => SendToEndPoint(url, content, logEvent, retryCount));
                }
                else
                {
                    Console.WriteLine("Exception - " + ex + " Log Event - " + logEvent + " retryCount - " + retryCount);
                }
            }
        }

        //Send Log File
        private static async Task SendFileToEndPoint(string filePath, string logEvent)
        {
            using MultipartFormDataContent form = new MultipartFormDataContent();
            try
            {
                using var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(filePath));
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");
                form.Add(fileContent, "file", Path.GetFileName(filePath));

                HttpResponseMessage response = await client.PostAsync(Environment.GetEnvironmentVariable("uploadDomain"), form);
                response.EnsureSuccessStatusCode();

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    Console.WriteLine("Error Occurred Processing event - " + logEvent + " HTTP Status Code - " + response.StatusCode);
                }
                else
                {
                    Console.WriteLine("Log event was sent to endpoint with status code - " + response.StatusCode + ", log path - " + filePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception - " + ex + "Log Event - " + logEvent);
            }
        }
    }
}