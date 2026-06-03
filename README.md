# Image Resizer

A web application for batch-uploading JPEG/PNG images and resizing them to a percentage of their original dimensions. The frontend is a React SPA; the backend is an ASP.NET Core 8 API that delegates resize work to a concurrent background worker and stores files in Azure Blob Storage.

---

## Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 8.0 |
| Node.js | 18+ |
| npm | 9+ |
| Azurite (local Azure Storage emulator) | any |

Install Azurite globally if you don't have it:

```bash
npm install -g azurite
```

---

## Local development

### 1. Start Azurite

```bash
azurite --silent --location .azurite --debug .azurite/debug.log
```

### 2. Run the backend

```bash
cd backend/ImageResizer.Api
dotnet run
```

The API listens on **http://localhost:5000**.  
Development configuration (`appsettings.Development.json`) connects to the local Azurite instance automatically.

### 3. Run the frontend

```bash
cd frontend
npm install
npm run dev
```

The dev server starts on **http://localhost:5173** and proxies all `/api` requests to the backend.

---

## Running tests

### Backend

```bash
cd backend/ImageResizer.Api.Tests
dotnet test
```

### Frontend

```bash
cd frontend
npm test          # single run
npm run test:watch  # watch mode
```

---

## Configuration

Backend configuration lives in `backend/ImageResizer.Api/appsettings*.json`.

| Key | Default (dev) | Description |
|-----|--------------|-------------|
| `AzureStorage:ConnectionString` | `UseDevelopmentStorage=true` | Azure Storage connection string |
| `BlobCleanup:IntervalMinutes` | `1` | How often the cleanup job runs |
| `BlobCleanup:ExpiryMinutes` | `5` | How long uploaded blobs are kept |

For a production deployment, set `AzureStorage:ConnectionString` to a real Azure Storage account connection string and adjust the cleanup intervals.

---

## Architecture

```
Browser (React SPA)
    │  POST /api/images/upload
    │  POST /api/images/resize   → ResizeJobQueue (bounded, 100 slots)
    │  GET  /api/images/resize/{id}/status
    │  GET  /api/images/download
    ▼
ASP.NET Core 8 API
    ├── BlobStorageService        Azure Blob Storage (originals + resized)
    ├── ImageFormatValidator      Magic-byte check (JPEG / PNG only)
    ├── ImageResizeService        SkiaSharp — pixel-budget guard, then resize
    ├── ResizeWorkerService       BackgroundService; Parallel.ForEachAsync
    └── BlobCleanupService        Deletes expired blobs on a configurable interval
```

### Three-step user flow

1. **Select & upload** — up to 10 images (20 MB each, JPEG/PNG only). Files are stored as originals in blob storage.
2. **Resize** — choose a percentage (0–100 % of original dimensions). The API enqueues a job and returns a `jobId`. The frontend polls the status endpoint until the job is `done` or `failed`, or the 2-minute deadline expires.
3. **Download** — links to the resized blobs are shown; each file is downloaded directly from the API.

### Limits enforced server-side

| Limit | Value |
|-------|-------|
| Files per upload request | 10 |
| File size | 20 MB |
| Pixel budget (decompression-bomb guard) | 100 MP |
| Resize jobs per request | 10 |
| Job queue capacity | 100 |
| Rate limit | 20 requests / minute / IP |
| Axios request timeout (frontend) | 30 s |
| Polling deadline (frontend) | 2 min |

### API endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/images/upload` | Upload images; returns blob names and per-file errors |
| `POST` | `/api/images/resize` | Enqueue resize job; returns `jobId` |
| `GET` | `/api/images/resize/{jobId}/status` | Poll job status (`queued` / `processing` / `done` / `failed`) |
| `GET` | `/api/images/download?blobName=…` | Stream a resized blob |
