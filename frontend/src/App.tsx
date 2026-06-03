import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import './App.scss'
import { type UploadError, downloadUrl, getResizeStatus, resizeImages, uploadImages } from './services/api'

type Phase = 'select' | 'uploaded' | 'resized'

export default function App() {
    const { t } = useTranslation()

    const [files, setFiles] = useState<File[]>([])
    const [phase, setPhase] = useState<Phase>('select')
    const [blobNames, setBlobNames] = useState<string[]>([])
    const [resizedBlobNames, setResizedBlobNames] = useState<string[]>([])
    const [percentage, setPercentage] = useState(50)
    const [uploading, setUploading] = useState(false)
    const [resizing, setResizing] = useState(false)
    const [error, setError] = useState<string | null>(null)
    const [uploadErrors, setUploadErrors] = useState<UploadError[]>([])

    const fileInputRef = useRef<HTMLInputElement>(null)

    const handleFilesChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const selected = Array.from(e.target.files ?? [])
        setFiles(selected)
        setPhase('select')
        setBlobNames([])
        setResizedBlobNames([])
        setUploadErrors([])
        setError(null)
    }

    const handleUpload = async () => {
        if (files.length === 0)
            return
        setUploading(true)
        setError(null)
        try {
            const result = await uploadImages(files)
            setUploadErrors(result.errors)
            if (result.blobNames.length > 0) {
                setBlobNames(result.blobNames)
                setPhase('uploaded')
            } else {
                setError(t('errors.allFilesFailed'))
            }
        }
        catch {
            setError(t('errors.uploadFailed'))
        }
        finally {
            setUploading(false)
        }
    }

    const handleResize = async () => {
        setResizing(true)
        setError(null)
        try {
            const jobId = await resizeImages(blobNames, percentage)
            while (true) {
                await new Promise(r => setTimeout(r, 1000))
                const result = await getResizeStatus(jobId)
                if (result.status === 'done') {
                    setResizedBlobNames(result.resizedBlobNames)
                    setPhase('resized')
                    break
                }
                if (result.status === 'failed') {
                    setError(result.error ?? t('errors.resizeFailed'))
                    break
                }
            }
        }
        catch {
            setError(t('errors.resizeFailed'))
        }
        finally {
            setResizing(false)
        }
    }

    const handleReset = () => {
        setFiles([])
        setBlobNames([])
        setResizedBlobNames([])
        setUploadErrors([])
        setPhase('select')
        setError(null)
        if (fileInputRef.current)
            fileInputRef.current.value = ''
    }

    return (
        <div className="app-container">
            <h1 className="title">{t('title')}</h1>

            {phase === 'select' && <section className="card">
                <h2 className="card-title">{t('step1.heading')}</h2>
                <input
                    className="file-input"
                    ref={fileInputRef}
                    type="file"
                    accept="image/jpeg,image/png"
                    multiple
                    onChange={handleFilesChange}
                    disabled={uploading}
                />
                <button
                    className="btn btn-file-select"
                    type="button"
                    onClick={() => fileInputRef.current?.click()}
                    disabled={uploading}
                >
                    {files.length > 0
                        ? t('step1.filesSelected', { count: files.length })
                        : t('step1.chooseImages')}
                </button>
                {files.length > 0 && (
                    <ul className="file-list">
                        {files.map(f => (
                            <li className="file-list-item" key={f.name + f.size}>
                                {f.name}
                                <span className="muted">{(f.size / 1024).toFixed(1)} KB</span>
                            </li>
                        ))}
                    </ul>
                )}
                <button
                    className="btn btn-primary"
                    onClick={handleUpload}
                    disabled={files.length === 0 || uploading}
                >
                    {uploading ? t('step1.uploading') : t('step1.upload')}
                </button>
            </section>}

            {phase === 'uploaded' && (
                <>
                    <section className="card">
                        <h2 className="card-title">{t('step2.heading')}</h2>
                        <p className="muted">{t('step2.imagesReady', { count: blobNames.length })}</p>
                        <label className="percent-row">
                            {t('step2.resizeTo')}
                            <input
                                className="percent-input"
                                type="number"
                                min={0}
                                max={100}
                                step={0.1}
                                value={percentage}
                                onChange={e => setPercentage(Math.max(0, Math.min(100, Number(e.target.value))))}
                            />
                            {t('step2.ofOriginalSize')}
                        </label>
                        <button
                            className="btn btn-primary"
                            onClick={handleResize}
                            disabled={resizing}
                        >
                            {resizing ? t('step2.resizing') : t('step2.resize')}
                        </button>
                    </section>
                    {uploadErrors.length > 0 && (
                        <>
                            <ul className="upload-error-list">
                                {uploadErrors.map(e => (
                                    <li className="upload-error-item" key={e.originalFileName}>
                                        <span>{e.originalFileName}</span>
                                        <span>{e.errorMessage}</span>
                                    </li>
                                ))}
                            </ul>
                            <span className="muted">{t('step2.uploadErrorCount', { count: uploadErrors.length })}</span>
                        </>
                    )}
                </>
            )}

            {phase === 'resized' && (
                <section className="card">
                    <h2 className="card-title">{t('step3.heading')}</h2>
                    <ul className="file-list">
                        {resizedBlobNames.map((name, i) => (
                            <li className="file-list-item" key={name}>
                                <a className="file-list-link" href={downloadUrl(name)} download>
                                    {files[i]?.name ?? t('step3.fallbackName', { index: i + 1 })}
                                </a>
                                <span className="muted">{t('step3.percentOfOriginal', { percentage })}</span>
                            </li>
                        ))}
                    </ul>
                </section>
            )}

            {error && <p className="error-message">{error}</p>}

            {phase !== 'select' && !(uploading || resizing) && (
                <button className="btn btn-secondary" onClick={handleReset}>
                    {t('actions.restart')}
                </button>
            )}
        </div>
    )
}
