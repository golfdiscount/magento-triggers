﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Azure.WebJobs;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Magento;

namespace magestack
{
    public class WsiTimer
    {
        [FunctionName("uploadOrders-timer")]
        [Singleton]
        public async Task Run(
            [TimerTrigger("45 15 * * *")]TimerInfo myTimer,
            ILogger log)
        {
            string today = DateTime.Today.ToString("MM/dd/yyyy");
            log.LogInformation($"Looking for WSI order files for {today}...");
            log.LogInformation("Connecting to SFTP server...");
            Magestack server = new Magestack();
            SftpClient sftp = server.CreateSftpClient();

            sftp.ChangeDir("var/export/mmexportcsv");
            List<Renci.SshNet.Sftp.SftpFile> files = sftp.List(
                pattern: "PT_WSI_" + string.Format("{0:MM_dd_yyy}", DateTime.Today)
            );

            log.LogInformation($"Found {files.Count} WSI files");
            log.LogInformation($"Disconnecting from {server.Host} server");
            server.Disconnect();
            log.LogInformation("Processing files");

            List<byte[]> fileByteArrays = ConvertFiles(files, sftp, log);
            log.LogInformation("Uploading to WSI API");

            HttpClient requester = new HttpClient();
            // Uploading to WSI can take a while, timeout is set to 5 minutes
            requester.Timeout = new TimeSpan(0, 5, 0);

            foreach (byte[] file in fileByteArrays)
            {
                await requester.PostAsync(Environment.GetEnvironmentVariable("wsi_url"), new ByteArrayContent(file));
            }
        }

        private List<byte[]> ConvertFiles(List<Renci.SshNet.Sftp.SftpFile> files, SftpClient sftp, ILogger log)
        {
            string result = "";
            List<byte[]> fileByteArrays = new List<byte[]>();
            foreach (Renci.SshNet.Sftp.SftpFile file in files)
            {
                log.LogInformation($"Writing {file.Name}");
                // Byte array of file contents
                byte[] fileContents = sftp.ReadFile(file);

                fileByteArrays.Add(fileContents);

                // Concatenated value of byte array (a singular file)
                string records = "";
                foreach (byte value in fileContents)
                {
                    records += ((char)value).ToString();
                }

                result += records;
            }

            return fileByteArrays;
        }
    }
}