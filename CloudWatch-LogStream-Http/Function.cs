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

        public static async Task<string> FunctionHandler(AwsRequestObject logs)
        {
            var response = "System Error Occurred Trying To Process CloudWatchLogStreamToHTTP. Check Inner Exception.";

            try
            {
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
                                await SendLogEvents(singleEvent.message);
                                response = "";
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex);
                                continue;
                            }
                        }
                    }
                    else if (Environment.GetEnvironmentVariable("postType") == "multi")
                    {
                        try
                        {
                            var messageList = decodeObj.logEvents.Where(x => x.message.Contains("_zl_timestamp")).Select(x => x.message).ToList();

                            var payload = new JsonSerializerSettings { Converters = { new SerializerFormatOverload("\n", "") } };

                            var payloadSetting = JsonConvert.SerializeObject(messageList, Formatting.None, payload);

                            var payloadFormat = payloadSetting.Replace("\"{", "{").Replace("}\"", "}");

                            var payloadRegex = Regex.Replace(payloadFormat, @"\\", "");

                            await SendLogEvents(payloadRegex);

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
            }
            catch (Exception ex)
            {
                Console.WriteLine("Response - " + response + " Exception Thrown - " + ex);
            }

            return response;
        }

        private static async Task SendLogEvents(string logEvent)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    var content = new StringContent(logEvent, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await client.PostAsync(Environment.GetEnvironmentVariable("uploadDomain"), content);

                    await response.Content.ReadAsStringAsync();

                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        Console.WriteLine("Error Occurred Processing event - " + logEvent + " HTTP Status Code - " + response.StatusCode);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception - " + ex + "Log Event - " + logEvent);
            }
        }
    }
}