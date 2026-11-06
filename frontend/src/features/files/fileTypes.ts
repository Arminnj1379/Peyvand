export interface Attachment {
  fileId: string;
  fileName: string;
  kind: 'Image' | 'Video' | 'Audio' | 'Document';
  contentType: string;
  sizeBytes: number;
  url: string;
}

/** Metadata returned by the upload endpoint (attach on send). */
export interface UploadedFile {
  id: string;
  fileName: string;
  kind: Attachment['kind'];
  contentType: string;
  sizeBytes: number;
  sha256: string;
  createdAt: string;
}

export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

export function toAttachment(meta: UploadedFile): Attachment {
  return {
    fileId: meta.id,
    fileName: meta.fileName,
    kind: meta.kind,
    contentType: meta.contentType,
    sizeBytes: meta.sizeBytes,
    url: `/api/v1/files/${meta.id}`,
  };
}
