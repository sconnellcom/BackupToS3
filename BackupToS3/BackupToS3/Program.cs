using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Amazon;
using Amazon.S3;
using Amazon.S3.Model;

namespace BackupToS3
{
    class Program
    {
        
        public static bool debug = false;
        public static StreamWriter LogWriter = null;

        static void Main(string[] args)
        {
            string BucketName = "", LocalPath = "", BucketFolder = "", LogFileName = "";
            bool SubFolders = false, CheckMD5 = false, Pause = false, PushDeletes = false, ExcludeCacheFolders = false;

            for (int x = 0; x < args.Length; x++)
            {
                string arg = args[x].ToLower();
                if (arg.Equals("-bucketname") && args.Length >= x + 1)
                {
                    BucketName = args[x + 1];
                }
                else if (arg.Equals("-localpath") && args.Length >= x + 1)
                {
                    LocalPath = args[x + 1];
                }
                else if (arg.Equals("-bucketfolder") && args.Length >= x + 1)
                {
                    BucketFolder = args[x + 1];
                }
                else if (arg.Equals("-subfolders"))
                {
                    SubFolders = true;
                }
                else if (arg.Equals("-checkmd5"))
                {
                    CheckMD5 = true;
                }
                else if (arg.Equals("-pause"))
                {
                    Pause = true;
                }
                else if (arg.Equals("-debug"))
                {
                    debug = true;
                }
                else if (arg.Equals("-pushdeletes"))
                {
                    PushDeletes = true;
                }
                else if (arg.Equals("-excludecachefolders"))
                {
                    ExcludeCacheFolders = true;
                }
                else if (arg.Equals("-logfilepath") && args.Length >= x + 1)
                {
                    LogFileName = args[x + 1];
                }
            }
            if (!string.IsNullOrEmpty(LogFileName))
            {
                using (LogWriter = File.AppendText(LogFileName))
                {
                    Log("Begin backup to S3\t" + string.Join(" ", args));
                    LogWriter.Flush();
                    try
                    {
                        BackupToS3(BucketName, LocalPath, BucketFolder, SubFolders, CheckMD5, PushDeletes, ExcludeCacheFolders);
                        Log("Backup to S3 complete\t" + string.Join(" ", args));
                    } catch (Exception e)
                    {
                        Log("Fatal Error! " + e.Message);
                        Log("Backup to S3 Ended in Error\t" + string.Join(" ", args));
                    }
                }
            }
            else
            {
                BackupToS3(BucketName, LocalPath, BucketFolder, SubFolders, CheckMD5, PushDeletes, ExcludeCacheFolders);
            }

            if (Pause) Console.ReadKey();

        }

        public static void Log(string Message)
        {
            Console.WriteLine(Message);
            if (LogWriter != null)
            {
                LogWriter.WriteLine("{0}\t{1}", DateTime.Now.ToString(), Message);
            }
        }
        
        static void BackupToS3(string BucketName, string LocalPath, string BucketFolder, bool SubFolders = false, bool CheckMD5 = false, bool PushDeletes = false, bool ExcludeCacheFolders = false){

            if (string.IsNullOrEmpty(BucketName) || string.IsNullOrEmpty(LocalPath))
            {
                Log("Error: Please specify a bucket and a local path.");
            }
            else
            {

                using (IAmazonS3 s3Client = AWSClientFactory.CreateAmazonS3Client())
                {

                    
                    if (!BucketExists(s3Client,BucketName))
                    {
                        Log("Error: Bucket not found.");
                    }else{

                        if (BucketFolder != "" && BucketFolder.Substring(BucketFolder.Length - 1) != "/") BucketFolder += "/";

                        // Get objects in bucket/folder
                        if (debug) { Log("Getting objects in S3 " + BucketName + "/" + BucketFolder); }
                        Dictionary<string, S3Object> S3Objects = GetS3Objects(s3Client, BucketName, BucketFolder, (SubFolders)?"":"/").ToDictionary(p => p.Key);
                        if (debug) { Log(S3Objects.Count + " objects found."); }

                        if (SubFolders)
                        {

                            BackupFolderToS3(s3Client, S3Objects, BucketName, LocalPath, BucketFolder, CheckMD5);

                            DirectoryInfo dir = new DirectoryInfo(LocalPath);
                            List<DirectoryInfo> dirs = dir.GetDirectories("*", SearchOption.AllDirectories).ToList();
                            //Log(dirs.Count() + " sub folders to backup");
                            if (ExcludeCacheFolders)
                            {
                                dirs = dirs.Where(p => !isCacheFolder(p.FullName.ToString())).ToList();
                                //Log(dirs.Count() + " sub folders to backup after removal of cache folders");
                            }

                            for (int x = 0; x < dirs.Count(); x++)
                            {
                                BackupFolderToS3(s3Client, S3Objects, BucketName, dirs[x].FullName, BucketFolder + dirs[x].FullName.Replace(dir.FullName + "\\", "").Replace("\\", "/") + "/", CheckMD5);
                            }
                        }
                        else
                        {
                            BackupFolderToS3(s3Client, S3Objects, BucketName, LocalPath, BucketFolder, CheckMD5);
                        }

                        if (PushDeletes) PushDeletesToS3(s3Client, S3Objects, BucketName, LocalPath, BucketFolder, ExcludeCacheFolders);

                    }

                }
            }

        }

        static void BackupFolderToS3(IAmazonS3 s3Client, Dictionary<string, S3Object> S3Objects, string BucketName, string LocalPath, string BucketFolder, bool CheckMD5)
        {

            // setup
            DirectoryInfo dir = new DirectoryInfo(LocalPath);
            FileInfo[] files = dir.GetFiles();
            
            if (debug) { 
                Log("Processing Folder: " + LocalPath);
                Log(files.Length + " files");
                Log("~~~ ~~~ ~~~ \n");
            }

            // process each local file
            for (int x = 0; x < files.Length; x++)
            {
                try
                {

                    bool uploadObject = false;
                    if (S3Objects.ContainsKey(BucketFolder + files[x].Name))
                    {
                        S3Object obj = S3Objects[BucketFolder + files[x].Name];
                        
                        // Upload the object if the local file is newer (modified date)
                        if (obj.LastModified <= files[x].LastWriteTime)
                        {
                            uploadObject = true;
                        }
                        else
                        {
                            // Upload the object if the MD5 hash is different.
                            if (CheckMD5)
                            {
                                string LocalMD5 = "\"" + GetMD5HashFromFile(files[x].FullName) + "\"";
                                if (obj.ETag != LocalMD5) uploadObject = true;
                            }
                        }
                    }
                    else
                    {
                        // Upload the object if it is not on S3
                        uploadObject = true;
                    }

                    // Upload object
                    if (uploadObject)
                    {
                        try
                        {
                            Log("Sending to backup\t" + BucketFolder + files[x].Name);
                            PutObjectRequest request = new PutObjectRequest();
                            request.BucketName = BucketName;
                            request.FilePath = files[x].FullName;
                            request.Key = BucketFolder + files[x].Name;
                            request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
                            request.StorageClass = S3StorageClass.ReducedRedundancy;
                            s3Client.PutObject(request);

                        }
                        catch (AmazonS3Exception ex)
                        {
                            if (ex.ErrorCode != null && (ex.ErrorCode.Equals("InvalidAccessKeyId") ||
                                ex.ErrorCode.Equals("InvalidSecurity")))
                            {
                                Log("Please check the provided AWS Credentials. If you haven't signed up for Amazon S3, please visit http://aws.amazon.com/s3");
                            }
                            else
                            {
                                Log("Failed to backup: " + ex.Message + "\t" + BucketFolder + files[x].Name);
                                if(debug){
                                    Log("Error uploading. Caught Exception: " + ex.Message);
                                    Log("Response Status Code: " + ex.StatusCode);
                                    Log("Error Code: " + ex.ErrorCode);
                                    Log("Request ID: " + ex.RequestId);
                                }
                            }
                        }
                    }
                }
                catch(Exception ex)
                {
                    Log("Failed to backup: " + ex.Message + "\t" + BucketFolder + files[x].Name);
                }
            }

        }


        static void PushDeletesToS3(IAmazonS3 s3Client, Dictionary<string, S3Object> S3Objects, string BucketName, string RootLocalPath, string RootBucketFolder, bool ExcludeCacheFolders)
        {
            if (debug)
            {
                Log(" Removing objects from S3 that have been deleted locally");
                Log("~~~ ~~~ ~~~ \n");
            }
            DirectoryInfo dir = new DirectoryInfo(RootLocalPath);
            Dictionary<string,FileInfo> files = dir.GetFiles("*",SearchOption.AllDirectories).ToDictionary(p=>p.FullName);
            if (ExcludeCacheFolders) files = files.Where(p => !isCacheFolder(p.Key.ToString())).ToDictionary(i => i.Key, i => i.Value);
            int ObjectsRemoved = 0;
            for (int x =0;x<S3Objects.Count();x++)
            {
                string s3Key = S3Objects.ElementAt(x).Key;
                string LocalPath = RootLocalPath + "\\" + s3Key.Substring(RootBucketFolder.Length).Replace("/", "\\");
                if (!files.ContainsKey(LocalPath))
                {
                    try {
                        Log("Removing from backup\t" + BucketName + "/" + s3Key);
                        DeleteObjectRequest deleteObjectRequest = new DeleteObjectRequest
                            {
                                BucketName = BucketName,
                                Key = s3Key
                            };
                        s3Client.DeleteObject(deleteObjectRequest);
                        ObjectsRemoved++;
                    }
                    catch (Exception ex)
                    {
                        Log("Failed to remove from backup: " + ex.Message + "\t" + BucketName + "/" + s3Key);
                    }
                }
            }
            if (debug)
            {
                Log("~~~ ~~~ ~~~ \n");
                Log(ObjectsRemoved + " objects removed from S3");
            }
        }

        static private bool BucketExists(IAmazonS3 s3Client, string BucketName)
        {
            // Check for bucket
            ListBucketsResponse response = s3Client.ListBuckets();
            bool bucket_found = false;
            foreach (S3Bucket bucket in response.Buckets)
            {
                if (bucket.BucketName == BucketName)
                {
                    bucket_found = true;
                    break;
                }
            }
            return bucket_found;
        }

        static private List<S3Object> GetS3Objects(IAmazonS3 s3Client, string BucketName, string BucketFolder, string Delimiter)
        {

            List<S3Object> S3Objects = new List<S3Object>();
            // Get objects in bucket/folder

            ListObjectsRequest request = new ListObjectsRequest()
                {
                    BucketName = BucketName,
                    // with Prefix is a folder Key, it will list only child of that folder
                    Prefix = BucketFolder,
                    Delimiter = Delimiter
                };
            do
            {
                ListObjectsResponse response = s3Client.ListObjects(request);
                S3Objects.AddRange(response.S3Objects);

                // If response is truncated, set the marker to get the next 
                // set of keys.
                if (response.IsTruncated)
                {
                    request.Marker = response.NextMarker;
                }
                else
                {
                    request = null;
                }
            } while (request != null);

            return S3Objects;
        }

        static private string GetMD5HashFromFile(string fileName)
        {
            FileStream file = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            System.Security.Cryptography.MD5 md5 = new System.Security.Cryptography.MD5CryptoServiceProvider();
            byte[] retVal = md5.ComputeHash(file);
            file.Close();

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < retVal.Length; i++)
            {
                sb.Append(retVal[i].ToString("x2"));
            }
            return sb.ToString();
        }

        static private bool isCacheFolder(string path)
        {
            return (isSubFolderOf(path, "wp-content/cache") || isSubFolderOf(path, "assets/cache") || isSubFolderOf(path, "uploads/cache"));
        }

        static private bool isSubFolderOf(string path, string subFolder)
        {
            path = path.Replace("/","\\") + "\\";
            string searchPath = "\\" + subFolder.Replace("/", "\\") + "\\";

            bool containsIt = path.IndexOf(searchPath, StringComparison.OrdinalIgnoreCase) > -1;

            return containsIt;
        }
    }
}
