#

# GDrive Backup background Service
Performs scheduled file backups, compresses them and sends them to GDrive. Supports running terminal commands before backups. Why not just use the official Google app? R: Because it only runs while the user is logged in and it also doesn't work well when sending database files.

# Quick start
1) Download Build files and place them in a preferred folder.
2) Starts terminal with administrador privileges
3) Run: sc create GDriveBackupBackgroundService binPath="C:\ProgramFolder\gdrive-backup-background-service.exe"
4) Rename appsettings.sample.json to appsettings.json and edit this configuration file by passing your gdrive and email details. Also specify which files you want to backup, as well as the time.
4) Open services.msc, search for GDriveBackupBackgroundService and start it.

# Appsettings.json sample

```
{
  "Enabled": true,
  "Directories": [], //Not implemented yet
  "Files": [ /* Specify files that you want backup */
    {
      "Origin": "C:\\Users\\leoho\\Downloads\\ASAHI.FBK",
      "TargetFolder": "/ALK-Backups"
    }
  ],
  "CommandsToCallBefore": [ "gbak -b -v -user sysdba -password masterkey C:\\Users\\leoho\\Downloads\\ASAHI.FDB C:\\Users\\leoho\\Downloads\\ASAHI.FBK" 
  ], //Commands to be called before. In the example command I made a backup of a database in firebird.
  "GoogleDrive": {
    "Enabled": true,
    "JustSimulate": false,
    "ClientSecret": "ARAAGenQ3CgYIARAAGenQ3CYIAR", //Use client secret for google console panel
    "RefreshToken": "enQ3CgYIARAAGenQ3CgYIARAAGenQ3CYIARAAG.apps.googleusercontent.com", //Use refresh token from google console panel
    "ApplicationName": "GDriveBackupService", //Use aplication name specified in google console panel
    "GDriveEmail": "example@test.com"
  },
  "StartsAt": {
    "Hours": 2, //Start hour
    "Minutes": 30, //Start minute
    "timezone": -3
  },
  "BackupIntervalDays": 7, //Backup interval
  "SendEmailsTo": [ "example@test.com" ], //target emails
  "EmailSettings": { //Smtp settings
    "Mail": "origin@test.com",
    "DisplayName": "Origin name",
    "Password": "Password",
    "Host": "smtp.gmail.com",
    "Port": 587,
    "bypass": false
  }
}
```