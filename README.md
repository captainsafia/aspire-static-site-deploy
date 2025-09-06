# Aspire Static Site Hosting

This project demonstrates how to use .NET Aspire to build and deploy a static website (built with Vite/TypeScript) to Azure Storage with static website hosting enabled.

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- [Node.js](https://nodejs.org/) (for building the static site)
- [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli) (for authentication)
- An Azure subscription
- [Aspire CLI daily](https://github.com/dotnet/aspire/blob/353aab478274d74eb3e924d6ef4e42ccda8f18ac/docs/using-latest-daily.md)

## Local Development

### Run the Aspire Application Locally

To run the Aspire orchestration locally:

```bash
aspire run
```

This will start the Aspire dashboard where you can monitor your application.

## Deployment to Azure

### 1. Azure Authentication

Make sure you're logged into Azure CLI:

```bash
az login
```

Set your subscription (if you have multiple):

```bash
az account set --subscription "your-subscription-id"
```

### 2. Publish Infrastructure (Optional)

Generate the Azure infrastructure files:

```bash
aspire publish
```

This command will:
- Generate Bicep templates for Azure resources
- Create configuration files for deployment
- Output files to the `generated/` directory

### 3. Deploy to Azure

Deploy the application and infrastructure to Azure:

```bash
aspire deploy
```

This command will:

1. **Build the static site**: Run `npm install` and `npm run build` in the `static-site` directory
2. **Deploy Azure infrastructure**: Create Azure Storage Account with static website hosting enabled
3. **Configure static website**: Enable static website hosting with `index.html` as the index document
4. **Upload files**: Upload the built static files to the `$web` container in Azure Storage

![screencapture of aspire deploy](./aspire-deploy.gif)

### Deployment Process Details

The deployment process includes several automated steps:

1. **Static Site Build**
   - Runs `npm install` to install dependencies
   - Runs `npm run build` to create production build in `dist/` folder

2. **Azure Storage Configuration**
   - Creates Azure Storage Account with public blob access enabled
   - Configures static website hosting with:
     - Index document: `index.html`
     - Error document: `index.html` (for SPA routing)

3. **File Upload**
   - Uploads all files from `static-site/dist/` to the `$web` container
   - Overwrites existing files to ensure latest version is deployed

The deployment creates:

- **Storage Account**: For hosting static files
- **Static Website Configuration**: Enables static website hosting on the storage account
- **$web Container**: Special container that serves as the website root
- **Azure Front Door**: For routing requests