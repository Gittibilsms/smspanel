using Microsoft.AspNetCore.Hosting;
using System;
using System.IO;

namespace GittBilSmsCore.Helpers
{
    public static class ReportPathHelper
    {
        public static string GetReportsRootPath(IWebHostEnvironment env)
        {
            // In Azure App Service → use D:\home\data\reports
            if (IsRunningInAzure())
            {
                return @"D:\home\data\reports";
            }
            else
            {
                // Local development → use under project folder
                return Path.Combine(env.ContentRootPath, "Reports");
            }
        }

        private static bool IsRunningInAzure()
        {
            var siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME");
            return !string.IsNullOrEmpty(siteName);
        }
    }
}