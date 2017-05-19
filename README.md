# BackupToS3
A Windows command line application to backup your local files to an AWS S3 Bucket written in C# .net

# Arguments
- -bucketname followed by the AWS bucket name to send the files to.
- -localpath  followed by the local path of the folder to send to S3.
- -bucketfolder followed by the folder in the AWS bucket to add the files/folders to.
- -subfolders If this argument exists, the sub folders in your localpath will be sent to S3.
- -checkmd5 If this argument exists, an MD5 of the local file will be created and checked against the MD5 available from AWS. Leaving this feature enabled takes significantly longer but ensures that your local files match the files on S3.
- -pause If this argument exists, the console app will wait for a keypress before exiting.
- -debug If this argument exists, additional information will be logged to the console/log file.
- -pushdeletes If this argument exists, files in your S3 bucket that do not exist locally will be deleted.
- -excludecachefolders If this argument exists, folders that match the listed cache folder names will not be pushed to S3, if they exist in S3, they will be removed.
- -logfilepath followed by the local path of a text file that you would like to save a log of the actions performed by this application.

# Features to Add
- Catch exceptions, log issues, example: access denied
- Test date modified, and S3 time zone
- gzip before upload, set status in metadata
- Multi thread?
