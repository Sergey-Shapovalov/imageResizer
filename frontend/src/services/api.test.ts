import { describe, it, expect } from 'vitest'
import { downloadUrl } from './api'

describe('downloadUrl', () => {
    it('returns the correct base URL', () => {
        expect(downloadUrl('photo.jpg')).toBe('/api/images/download?blobName=photo.jpg')
    })

    it('percent-encodes spaces in blob names', () => {
        expect(downloadUrl('my photo.jpg')).toBe('/api/images/download?blobName=my%20photo.jpg')
    })

    it('preserves underscores in the resized blob name suffix', () => {
        expect(downloadUrl('abc123_photo_resized.jpg')).toBe(
            '/api/images/download?blobName=abc123_photo_resized.jpg'
        )
    })
})
