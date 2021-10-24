﻿using Magento;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace magestack
{
    public class WsiHttp
    {
        private readonly SftpClient _sftp;

        public WsiHttp(SftpClient sftp)
        {
            _sftp = sftp;
        }

        [FunctionName("uploadOrders")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            string today = DateTime.Today.ToString("MM/dd/yyyy");
            log.LogInformation($"Looking for WSI order files for {today}...");

            if (_sftp.WorkingDirectory != "/microcloud/domains/golfdi/domains/golfdiscount.com/http/var/export/mmexportcsv")
            {
                _sftp.ChangeDir("var/export/mmexportcsv");
            }

            List<Renci.SshNet.Sftp.SftpFile> files = _sftp.List(
                pattern: "PT_WSI_" + string.Format("{0:MM_dd_yyy}", DateTime.Today)
            );

            log.LogInformation($"Found {files.Count} WSI files");
            if (files.Count != 0)
            {
                log.LogInformation("Processing files");

                Queue<Renci.SshNet.Sftp.SftpFile> recentFiles = _sftp.RecentFiles;

                // Check any files have been recently uploaded
                foreach (Renci.SshNet.Sftp.SftpFile file in files)
                {
                    if (recentFiles.Contains(file))
                    {
                        log.LogWarning($"{file.Name} has already been processed");
                        // Remove file at the list index of file in queue already
                        files.RemoveAt(files.FindIndex(recentFile => recentFile.Name == file.Name));
                    }
                    else
                    {
                        if (recentFiles.Count == 10)
                        {
                            recentFiles.Dequeue();
                        }

                        recentFiles.Enqueue(file);
                    }
                }

                Dictionary<string, byte[]> fileBytes = ConvertFiles(files, log);
                log.LogInformation("Uploading to WSI API");

                // Uploading to WSI can take a while as each record is inserted into a DB, request timeout is set to 5 minutes
                HttpClient requester = new HttpClient {
                    Timeout = new TimeSpan(0, 10, 0)
                };

                foreach (KeyValuePair<string, byte[]> file in fileBytes)
                {
                    log.LogInformation($"Sending file {file.Key}");
                    HttpResponseMessage res = await requester.PostAsync(Environment.GetEnvironmentVariable("wsi_url"), new ByteArrayContent(file.Value));

                    if (res.IsSuccessStatusCode)
                    {
                        log.LogInformation($"Successfully uploaded {file.Key}");
                    } else
                    {
                        log.LogWarning($"There was an issue uploading {file.Key}, please check the logs");
                        string error = await res.Content.ReadAsStringAsync();
                        log.LogWarning(error);
                        return new BadRequestObjectResult($"There was an issue uploading {file.Key}: {error}");
                    }
                }
            } else
            {
                log.LogWarning("There were no WSI files to upload");
            }
            return new OkObjectResult($"{files.Count} file(s) processed and uploaded successfully");
        }

        private Dictionary<string, byte[]> ConvertFiles(List<Renci.SshNet.Sftp.SftpFile> files, ILogger log)
        {
            Dictionary<string, byte[]> fileBytes = new Dictionary<string, byte[]>();
            foreach (Renci.SshNet.Sftp.SftpFile file in files)
            {
                log.LogInformation($"Writing {file.Name}");
                // Byte array of file contents
                fileBytes.Add(file.Name, _sftp.ReadFile(file));
            }

            return fileBytes;
        }
    }
}