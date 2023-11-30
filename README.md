# API

Welcome to the backend API for 'Simplifying Small Business Payments.'

## Before You Begin

To set up and run the application locally, follow these steps:

### Windows Setup

1. **Create User Secrets**
    - Navigate to `C:\%USERPROFILE%\AppData\Roaming\Microsoft\UserSecrets` and create a folder
      named `41019bab-28dd-41f1-a221-82ed1e906626`.
    - Create a file named `secrets.json`.
    - Add the following JSON to the file:

```
{
"DatabaseSettings": {
    "ConnectionString": "Server=kluster.database;Database=KlusterDB;User Id=SA;Password={Password};
            MultipleActiveResultSets=true;TrustServerCertificate=true;"
        },
    "JwtSettings": {
            "Audience": "{Audience}",
            "Issuer": "{Issuer}",
            "SecretKey": "{SecretKey}",
            "TokenLifetimeInHours": 1
        },
    "RabbitMqSettings": {
        "Host": "kluster.messaging",
        "Password": "{Password}",
        "Username": "{Password}"
    },
    "MailSettings": {
        "DisplayName": "{FirstName} {LastName}",
        "From": "{EmailAddress}",
        "Host": "{Host}",
        "Password": "{Password}",
        "Port": {Port},
        "UserName": "{Username}",
        "UseSsl": false,
        "UseStartTls": true
    },
    KeyVault": {
        "AZURE_CLIENT_ID": "",
        "AZURE_CLIENT_SECRET": "",
        "AZURE_STORAGE_CONNECTION_STRING": "",
        "AZURE_TENANT_ID": "",
        "BLOB_CONTAINER_NAME": "",
        "Vault": "kluster-api"
    },
    "PaystackSettings": {
        "AllowedIPs": [],
        "BaseUrl": "",
        "PublicKey": "",
        "SecretKey": ""
    },
    "SeqSettings": {
        "BaseUrl": "",
		"ApiKey":""
    }
}
```

2. **Run the Application**
    - Navigate to the `Kluster.Host/` directory in the project.
    - Execute `docker compose up`.

### macOS Setup
1. **Create User Secrets**
   - Navigate to `~/.microsoft/usersecrets` and create a folder named `41019bab-28dd-41f1-a221-82ed1e906626`.
   - Create a file named `secrets.json`.
   - Add the same JSON content provided earlier.

2. **Run the Application**  
   - Navigate to the Kluster.Host directory in the project.
   - Run docker compose up.

## Important Notes
- After running the command, three containers will start: `kluster-api`, `kluster-database` and `kluster.messaging`.
- Access the Swagger documentation at [Swagger](http://localhost:5000/swagger/index.html).
- Send requests to the API at http://localhost:5000/api.
- If required folders are missing, ensure to create them.

You can access the postman collection here, with examples: [PostmanDoc](https://documenter.getpostman.com/view/22039666/2s9YeAAumQ).
