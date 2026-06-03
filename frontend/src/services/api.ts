import axios from 'axios'

const client = axios.create({ baseURL: '/api/images', timeout: 30_000 })

export interface UploadError {
  originalFileName: string
  errorMessage: string
}

export interface UploadResult {
  filesUploaded: number
  blobNames: string[]
  errors: UploadError[]
}

export async function uploadImages(files: File[]): Promise<UploadResult> {
  const form = new FormData()
  files.forEach(f => form.append('files', f))
  const { data } = await client.post<UploadResult>('/upload', form)
  return data
}

export type ResizeStatus = 'queued' | 'processing' | 'done' | 'failed'

export interface ResizeStatusResult {
  status: ResizeStatus
  resizedBlobNames: string[]
  error?: string
}

export async function resizeImages(blobNames: string[], percentage: number): Promise<string> {
  const { data } = await client.post<{ jobId: string }>('/resize', { blobNames, percentage })
  return data.jobId
}

export async function getResizeStatus(jobId: string): Promise<ResizeStatusResult> {
  const { data } = await client.get<ResizeStatusResult>(`/resize/${jobId}/status`)
  return data
}

export function downloadUrl(blobName: string): string {
  return `/api/images/download?blobName=${encodeURIComponent(blobName)}`
}
