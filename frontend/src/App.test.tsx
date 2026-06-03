import { render, screen, fireEvent, waitFor, act } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import App from './App'
import * as api from './services/api'

vi.mock('./services/api', () => ({
    uploadImages: vi.fn(),
    resizeImages: vi.fn(),
    getResizeStatus: vi.fn(),
    downloadUrl: vi.fn(),
}))

const mockUpload = vi.mocked(api.uploadImages)
const mockResize = vi.mocked(api.resizeImages)
const mockStatus = vi.mocked(api.getResizeStatus)
const mockDownloadUrl = vi.mocked(api.downloadUrl)

function selectFiles(...names: string[]) {
    const input = document.querySelector('input[type="file"]') as HTMLInputElement
    const files = names.map(n => new File(['data'], n, { type: 'image/jpeg' }))
    fireEvent.change(input, { target: { files } })
}

describe('App', () => {
    beforeEach(() => {
        vi.clearAllMocks()
        mockDownloadUrl.mockImplementation(
            (name: string) => `/api/images/download?blobName=${encodeURIComponent(name)}`
        )
    })

    afterEach(() => vi.useRealTimers())

    // ── Step 1: select ────────────────────────────────────────────────────────

    it('renders the select phase on load', () => {
        render(<App />)
        expect(screen.getByText(/Step 1/)).toBeInTheDocument()
        expect(screen.getByText('Choose images')).toBeInTheDocument()
    })

    it('upload button is disabled when no files are selected', () => {
        render(<App />)
        expect(screen.getByRole('button', { name: 'Upload' })).toBeDisabled()
    })

    it('select button shows file count after files are chosen', () => {
        render(<App />)
        selectFiles('a.jpg', 'b.jpg')
        expect(screen.getByText('2 file(s) selected')).toBeInTheDocument()
    })

    it('lists selected file names', () => {
        render(<App />)
        selectFiles('photo.jpg', 'portrait.png')
        expect(screen.getByText('photo.jpg')).toBeInTheDocument()
        expect(screen.getByText('portrait.png')).toBeInTheDocument()
    })

    // ── Upload ────────────────────────────────────────────────────────────────

    it('transitions to Step 2 on successful upload', async () => {
        mockUpload.mockResolvedValue({ filesUploaded: 1, blobNames: ['blob1'], errors: [] })
        render(<App />)
        selectFiles('photo.jpg')
        fireEvent.click(screen.getByRole('button', { name: 'Upload' }))
        await waitFor(() => expect(screen.getByText(/Step 2/)).toBeInTheDocument())
    })

    it('shows a generic error when the upload request throws', async () => {
        mockUpload.mockRejectedValue(new Error('Network error'))
        render(<App />)
        selectFiles('photo.jpg')
        fireEvent.click(screen.getByRole('button', { name: 'Upload' }))
        await waitFor(() => expect(screen.getByText(/upload failed/i)).toBeInTheDocument())
    })

    it('shows "all files failed" and stays on Step 1 when no blobs succeed', async () => {
        mockUpload.mockResolvedValue({
            filesUploaded: 0,
            blobNames: [],
            errors: [{ originalFileName: 'bad.gif', errorMessage: 'Unsupported format' }],
        })
        render(<App />)
        selectFiles('bad.gif')
        fireEvent.click(screen.getByRole('button', { name: 'Upload' }))
        await waitFor(() =>
            expect(screen.getByText('All files failed to upload.')).toBeInTheDocument()
        )
        expect(screen.queryByText(/Step 2/)).not.toBeInTheDocument()
    })

    it('advances to Step 2 even when only some files succeed', async () => {
        mockUpload.mockResolvedValue({
            filesUploaded: 1,
            blobNames: ['blob1'],
            errors: [{ originalFileName: 'bad.gif', errorMessage: 'Unsupported format' }],
        })
        render(<App />)
        selectFiles('good.jpg', 'bad.gif')
        fireEvent.click(screen.getByRole('button', { name: 'Upload' }))
        await waitFor(() => expect(screen.getByText(/Step 2/)).toBeInTheDocument())
    })

    // ── Resize ────────────────────────────────────────────────────────────────

    async function goToStep2() {
        mockUpload.mockResolvedValue({ filesUploaded: 1, blobNames: ['blob1'], errors: [] })
        render(<App />)
        selectFiles('photo.jpg')
        fireEvent.click(screen.getByRole('button', { name: 'Upload' }))
        await waitFor(() => screen.getByText(/Step 2/))
    }

    it('transitions to Step 3 when resize job completes', async () => {
        await goToStep2()
        mockResize.mockResolvedValue('job-1')
        mockStatus.mockResolvedValue({ status: 'done', resizedBlobNames: ['blob1_resized'], error: undefined })

        vi.useFakeTimers()
        await act(async () => {
            fireEvent.click(screen.getByRole('button', { name: 'Resize' }))
            await vi.runAllTimersAsync()
        })

        expect(screen.getByText(/Step 3/)).toBeInTheDocument()
    })

    it('shows queue-full message when the resize request returns 503', async () => {
        await goToStep2()
        mockResize.mockRejectedValue(
            Object.assign(new Error(), { isAxiosError: true, response: { status: 503 } })
        )
        fireEvent.click(screen.getByRole('button', { name: 'Resize' }))
        await waitFor(() => expect(screen.getByText(/busy/i)).toBeInTheDocument())
    })

    it('shows rate-limited message when the resize request returns 429', async () => {
        await goToStep2()
        mockResize.mockRejectedValue(
            Object.assign(new Error(), { isAxiosError: true, response: { status: 429 } })
        )
        fireEvent.click(screen.getByRole('button', { name: 'Resize' }))
        await waitFor(() => expect(screen.getByText(/too many requests/i)).toBeInTheDocument())
    })

    it('shows the server error message when resize job fails', async () => {
        await goToStep2()
        mockResize.mockResolvedValue('job-1')
        mockStatus.mockResolvedValue({ status: 'failed', resizedBlobNames: [], error: 'Disk full' })

        vi.useFakeTimers()
        await act(async () => {
            fireEvent.click(screen.getByRole('button', { name: 'Resize' }))
            await vi.runAllTimersAsync()
        })

        expect(screen.getByText('Disk full')).toBeInTheDocument()
    })

    it('polls status until the job finishes', async () => {
        await goToStep2()
        mockResize.mockResolvedValue('job-1')
        mockStatus
            .mockResolvedValueOnce({ status: 'queued',     resizedBlobNames: [], error: undefined })
            .mockResolvedValueOnce({ status: 'processing', resizedBlobNames: [], error: undefined })
            .mockResolvedValueOnce({ status: 'done', resizedBlobNames: ['blob1_resized'], error: undefined })

        vi.useFakeTimers()
        await act(async () => {
            fireEvent.click(screen.getByRole('button', { name: 'Resize' }))
            await vi.runAllTimersAsync()
        })

        expect(screen.getByText(/Step 3/)).toBeInTheDocument()
        expect(mockStatus).toHaveBeenCalledTimes(3)
    })

    it('renders download links in Step 3', async () => {
        await goToStep2()
        mockResize.mockResolvedValue('job-1')
        mockStatus.mockResolvedValue({ status: 'done', resizedBlobNames: ['blob1_resized'], error: undefined })

        vi.useFakeTimers()
        await act(async () => {
            fireEvent.click(screen.getByRole('button', { name: 'Resize' }))
            await vi.runAllTimersAsync()
        })

        const link = screen.getByRole('link', { name: 'photo.jpg' })
        expect(link).toHaveAttribute('href', '/api/images/download?blobName=blob1_resized')
    })

    // ── Reset ─────────────────────────────────────────────────────────────────

    it('returns to Step 1 and resets state after Restart', async () => {
        mockUpload.mockResolvedValue({ filesUploaded: 1, blobNames: ['blob1'], errors: [] })
        render(<App />)
        selectFiles('photo.jpg')
        fireEvent.click(screen.getByRole('button', { name: 'Upload' }))
        await waitFor(() => screen.getByText(/Step 2/))

        fireEvent.click(screen.getByRole('button', { name: 'Restart' }))

        expect(screen.getByText(/Step 1/)).toBeInTheDocument()
        expect(screen.getByText('Choose images')).toBeInTheDocument()
    })
})
