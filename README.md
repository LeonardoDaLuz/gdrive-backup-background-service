#

# GDrive Backup background Service
Performs scheduled file backups, compresses them and sends them to GDrive. Supports running terminal commands before backups. Why not just use the official Google app? R: Because it only runs while the user is logged in and it also doesn't work well when sending database files.

# Quick start on windows
1) Download Build files and place them in a preferred folder.
2) Starts terminal with administrador privileges
3) Run: sc create GDriveBackupBackgroundService binPath="C:\ProgramFolder\gdrive-backup-background-service.exe"
4) Rename appsettings.sample.json to appsettings.json and edit this configuration file by passing your gdrive and email details. Also specify which files you want to backup, as well as the time.
4) Open services.msc, search for GDriveBackupBackgroundService and start it.

# Quick start on linux
Put this file in: cat /etc/systemd/system/gdrivebackup.service:
```
[Unit]
Description=Gdrive backup for Klinny.net api

[Service]
WorkingDirectory=/usr/local/GdriveBackupBackgroundService
ExecStart=/usr/local/GdriveBackupBackgroundService/gdrive-backup-background-service
Restart=aways
#restart service after 10 seconds if the dotnet service crashes
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=GdriveBackup
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```
Run commands:
```
sudo systemctl daemon-reload
sudo systemctl enable gdrivebackup.service
sudo systemctl start gdrivebackup.service
sudo systemctl status gdrivebackup.service
```
now you can use sudo service gdrive status, start or stop.

# Appsettings.json sample

```
{
  "Enabled": true,
  "Directories": [], //Not implemented yet
  "FilesOrDirectory": [  /* Specify files that you want backup */
    {
      "Origin": "/tmp/klinny_dump",
      "TargetFolder": "/KLINNY_Backups/DatabaseExports",
      "CommandsToCallBefore": [ "mkdir -p /tmp/klinny_dump && cd /tmp/klinny_dump && sudo mysqldump -u \"userProducao\" -pPass*123 --no-tablespaces latitude > backup.sql && zip backup-$(date +\"%Y-%m-%d\").sql.zip backup.sql ; rm backup.sql" ], //Commands to be called before. In the example command I made a backup of a database in mysql.
      "EmailTitle": "Klinny - Backup banco de dados"
    },
    {
      "Origin": "/var/www/klinnyApi/PrivateFiles",
      "TargetFolder": "/KLINNY_Backups/PrivateFiles",
      "CommandsToCallBefore": [],
      "EmailTitle": "Klinny - Backup arquivos privados"
    },
    {
      "Origin": "/var/www/klinnyApi/wwwroot",
      "TargetFolder": "/KLINNY_Backups/wwwroot",
      "CommandsToCallBefore": [],
      "EmailTitle": "Klinny - Backup arquivos públicos"
    },
    {
      "Origin": "C:\\Users\\leoho\\Downloads\\ASAHI.FBK",
      "TargetFolder": "/ALK-Backups"
      "CommandsToCallBefore": ["gbak -b -v -user sysdba -password masterkey C:\\Users\\leoho\\Downloads\\ASAHI.FDB C:\\Users\\leoho\\Downloads\\ASAHI.FBK"],//Commands to be called before. In the example command I made a backup of a database in firebird.
      "EmailTitle": "Klinny - Backup arquivos públicos"
    }
  ],
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
  "ForceToRunOnStartup": true, /* Force to run on start, for testing purposes */
  "BackupIntervalDays": 1, //Backup interval
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