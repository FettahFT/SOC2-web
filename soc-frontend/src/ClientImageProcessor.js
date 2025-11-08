/**
 * Client-side Image Processor for Steganography
 * Ports the C# ImageProcessor.cs logic to JavaScript using Canvas API and Web Crypto API
 */
class ClientImageProcessor {
  static SIGNATURE = "SC";
  static MAX_FILE_SIZE = 10 * 1024 * 1024; // 10MB
  static BYTES_PER_PIXEL = 4;
  static SHA256_SIZE = 32;
  static CHUNK_SIZE = 1024 * 1024; // 1MB chunks for memory management

  /**
   * Create a carrier image with hidden file data
   * @param {File} file - The file to hide
   * @param {string} password - Optional password for encryption
   * @param {function} onProgress - Progress callback (0-100)
   * @returns {Promise<Blob>} PNG blob containing the hidden data
   */
  static async createCarrierImageAsync(file, password = null, onProgress = null) {
    const fileData = await this.readFileAsArrayBuffer(file);
    const fileName = file.name;
    const isEncrypted = password != null;

    if (fileData.byteLength > this.MAX_FILE_SIZE) {
      throw new Error(`File too large. Maximum size is ${this.MAX_FILE_SIZE / (1024 * 1024)}MB.`);
    }

    onProgress?.(10);

    // Compute SHA256 hash
    const sha256Hash = await this.computeSHA256(fileData);
    onProgress?.(20);

    // Encrypt if password provided
    let processedData = fileData;
    if (isEncrypted) {
      processedData = await this.encryptData(fileData, password);
      onProgress?.(30);
    }

    // Create header
    const header = this.createHeader(processedData.byteLength, fileName, sha256Hash, isEncrypted);
    console.log('Header created, length:', header.length, 'First 10 bytes:', header.slice(0, 10));
    onProgress?.(40);

    // Combine header and data
    const totalData = new Uint8Array(header.length + processedData.byteLength);
    totalData.set(header, 0);
    totalData.set(new Uint8Array(processedData), header.length);
    console.log('Total data length:', totalData.length, 'First 10 bytes:', totalData.slice(0, 10));
    onProgress?.(50);

    // Calculate image dimensions
    const pixelCount = Math.ceil(totalData.length / this.BYTES_PER_PIXEL);
    const imageSize = Math.ceil(Math.sqrt(pixelCount));
    console.log('Pixel count:', pixelCount, 'Image size:', imageSize, 'Total pixels needed:', imageSize * imageSize);
    onProgress?.(60);

    // Create canvas and encode data
    const canvas = document.createElement('canvas');
    canvas.width = imageSize;
    canvas.height = imageSize;
    const ctx = canvas.getContext('2d');

    // Fill with white background
    ctx.fillStyle = 'white';
    ctx.fillRect(0, 0, imageSize, imageSize);

    // Encode data into pixels
    const imageData = ctx.getImageData(0, 0, imageSize, imageSize);
    this.writeBytesToImageData(imageData.data, totalData);
    ctx.putImageData(imageData, 0, 0);
    onProgress?.(90);

    // Convert to PNG blob
    return new Promise((resolve) => {
      canvas.toBlob((blob) => {
        onProgress?.(100);
        resolve(blob);
      }, 'image/png');
    });
  }

  /**
   * Extract file from carrier image
   * @param {File} imageFile - PNG file containing hidden data
   * @param {string} password - Optional password for decryption
   * @param {function} onProgress - Progress callback (0-100)
   * @returns {Promise<{fileName: string, data: Uint8Array}>}
   */
  static async extractFileAsync(imageFile, password = null, onProgress = null) {
    const img = new Image();
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d');

    return new Promise((resolve, reject) => {
      img.onload = async () => {
        canvas.width = img.width;
        canvas.height = img.height;
        ctx.drawImage(img, 0, 0);

        try {
          const imageData = ctx.getImageData(0, 0, img.width, img.height);
          const pixelData = imageData.data;

          onProgress?.(20);

          // Read header
          const headerInfo = this.readHeader(pixelData);
          console.log('Header info:', headerInfo);
          onProgress?.(40);

          // Read file data
          const fileData = this.readFileData(pixelData, headerInfo.totalHeaderSize, headerInfo.fileSize);
          console.log('File data length:', fileData.length, 'Expected:', headerInfo.fileSize);
          onProgress?.(60);

          // Verify hash
          const computedHash = await this.computeSHA256(fileData);
          if (!this.arraysEqual(computedHash, headerInfo.sha256Hash)) {
            throw new Error('SHA256 hash mismatch. File may be corrupted.');
          }
          onProgress?.(80);

          // Decrypt if needed
          let finalData = fileData;
          if (headerInfo.isEncrypted) {
            if (!password) {
              throw new Error('File is encrypted but no password provided.');
            }
            finalData = await this.decryptData(fileData, password);
          }

          onProgress?.(100);
          resolve({
            fileName: headerInfo.fileName,
            data: finalData
          });
        } catch (error) {
          reject(error);
        }
      };

      img.onerror = () => reject(new Error('Failed to load image'));
      img.src = URL.createObjectURL(imageFile);
    });
  }

  /**
   * Extract metadata from carrier image without file data
   * @param {File} imageFile - PNG file
   * @returns {Promise<Object>} Metadata object
   */
  static async extractMetadataAsync(imageFile) {
    const img = new Image();
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d');

    return new Promise((resolve, reject) => {
      img.onload = () => {
        console.log('Loaded image dimensions:', img.width, 'x', img.height);
        canvas.width = img.width;
        canvas.height = img.height;
        ctx.drawImage(img, 0, 0);

        try {
          const imageData = ctx.getImageData(0, 0, img.width, img.height);
          const pixelData = imageData.data;
          console.log('Canvas imageData length:', pixelData.length, 'first 16 bytes:', pixelData.slice(0, 16));

          const headerInfo = this.readHeader(pixelData);

          resolve({
            signature: headerInfo.signature,
            fileSize: headerInfo.fileSize,
            fileName: headerInfo.fileName,
            sha256: Array.from(headerInfo.sha256Hash).map(b => b.toString(16).padStart(2, '0')).join(''),
            isEncrypted: headerInfo.isEncrypted
          });
        } catch (error) {
          reject(error);
        }
      };

      img.onerror = () => reject(new Error('Failed to load image'));
      img.src = URL.createObjectURL(imageFile);
    });
  }

  // Helper methods
  static async readFileAsArrayBuffer(file) {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result);
      reader.onerror = () => reject(new Error('Failed to read file'));
      reader.readAsArrayBuffer(file);
    });
  }

  static async computeSHA256(data) {
    const hashBuffer = await crypto.subtle.digest('SHA-256', data);
    return new Uint8Array(hashBuffer);
  }

  static createHeader(fileSize, fileName, sha256Hash, isEncrypted) {
    const fileNameBytes = new TextEncoder().encode(fileName);
    if (fileNameBytes.length > 255) {
      throw new Error('Filename too long');
    }

    const baseHeaderSize = 2 + 8 + 4; // signature + fileSize + fileNameLength
    const isEncryptedByte = new Uint8Array([isEncrypted ? 1 : 0]);
    const currentSize = baseHeaderSize + fileNameBytes.length + 1; // +1 for isEncrypted
    const padding = (4 - (currentSize % 4)) % 4;
    const totalHeaderSize = currentSize + padding + this.SHA256_SIZE;

    const header = new Uint8Array(totalHeaderSize);
    let offset = 0;

    // Signature
    header.set(new TextEncoder().encode(this.SIGNATURE), offset);
    offset += 2;

    // File size
    const sizeBytes = new ArrayBuffer(8);
    new DataView(sizeBytes).setUint32(0, fileSize & 0xFFFFFFFF, true); // little-endian low 32 bits
    new DataView(sizeBytes).setUint32(4, (fileSize >>> 32) & 0xFFFFFFFF, true); // high 32 bits
    header.set(new Uint8Array(sizeBytes), offset);
    offset += 8;

    // Filename length
    const nameLenBytes = new ArrayBuffer(4);
    new DataView(nameLenBytes).setUint32(0, fileNameBytes.length, true);
    header.set(new Uint8Array(nameLenBytes), offset);
    offset += 4;

    // Filename
    header.set(fileNameBytes, offset);
    offset += fileNameBytes.length;

    // IsEncrypted
    header.set(isEncryptedByte, offset);
    offset += 1;

    // Padding
    offset += padding;

    // SHA256 hash
    header.set(sha256Hash, offset);

    return header;
  }

  static readHeader(pixelData) {
    // Read signature (first 2 bytes)
    const signatureBytes = this.readBytesFromPixelData(pixelData, 0, 2);
    const signature = String.fromCharCode(signatureBytes[0], signatureBytes[1]);
    console.log('Signature bytes read:', signatureBytes, 'as chars:', signature);

    if (signature !== this.SIGNATURE) {
      throw new Error(`Invalid signature '${signature}'. This is not a ShadeOfColor2 encoded image.`);
    }

    // Read file size (8 bytes starting at offset 2)
    const fileSizeBytes = this.readBytesFromPixelData(pixelData, 2, 8);
    const fileSize = new DataView(fileSizeBytes.buffer).getUint32(0, true) +
                     (new DataView(fileSizeBytes.buffer).getUint32(4, true) * 0x100000000);

    // Read filename length (4 bytes starting at offset 10)
    const fileNameLengthBytes = this.readBytesFromPixelData(pixelData, 10, 4);
    const fileNameLength = new DataView(fileNameLengthBytes.buffer).getUint32(0, true);

    // Read filename
    const fileNameBytes = this.readBytesFromPixelData(pixelData, 14, fileNameLength);
    const fileName = new TextDecoder().decode(fileNameBytes);

    // Read isEncrypted (1 byte)
    const isEncryptedBytes = this.readBytesFromPixelData(pixelData, 14 + fileNameLength, 1);
    const isEncrypted = isEncryptedBytes[0] === 1;

    // Calculate SHA256 offset
    const headerWithoutHash = 2 + 8 + 4 + fileNameLength + 1;
    const sha256Offset = headerWithoutHash + (4 - (headerWithoutHash % 4)) % 4;

    // Read SHA256 hash
    const sha256Hash = this.readBytesFromPixelData(pixelData, sha256Offset, this.SHA256_SIZE);

    return {
      signature,
      fileSize: Number(fileSize),
      fileName,
      isEncrypted,
      sha256Hash,
      totalHeaderSize: sha256Offset + this.SHA256_SIZE
    };
  }

  static readBytesFromPixelData(pixelData, startIndex, length) {
    const bytes = new Uint8Array(length);
    console.log(`Reading ${length} bytes from pixelData starting at ${startIndex}`);
    for (let i = 0; i < length; i++) {
      const pixelIndex = startIndex + i;
      const pixelOffset = pixelIndex % 4;
      const pixelPosition = Math.floor(pixelIndex / 4);
      const arrayIndex = pixelPosition * 4 + pixelOffset;

      if (arrayIndex < pixelData.length) {
        bytes[i] = pixelData[arrayIndex];
      }
      if (i < 10) console.log(`Byte ${i}: pixelIndex=${pixelIndex}, offset=${pixelOffset}, position=${pixelPosition}, arrayIndex=${arrayIndex}, value=${bytes[i]}`);
    }
    console.log(`Read bytes:`, bytes.slice(0, Math.min(10, length)));
    return bytes;
  }

  static readFileData(pixelData, startOffset, fileSize) {
    const data = new Uint8Array(fileSize);
    for (let i = 0; i < fileSize; i++) {
      const pixelIndex = startOffset + i;
      const pixelOffset = pixelIndex % 4;
      const pixelPosition = Math.floor(pixelIndex / 4);
      const arrayIndex = pixelPosition * 4 + pixelOffset;

      if (arrayIndex < pixelData.length) {
        data[i] = pixelData[arrayIndex];
      }
    }
    return data;
  }

  static writeBytesToImageData(imageData, bytes) {
    console.log('Writing bytes to imageData, first 10 bytes:', bytes.slice(0, 10));
    for (let i = 0; i < bytes.length; i++) {
      const pixelIndex = i;
      const pixelOffset = pixelIndex % 4;
      const pixelPosition = Math.floor(pixelIndex / 4);
      const arrayIndex = pixelPosition * 4 + pixelOffset;

      if (arrayIndex < imageData.length) {
        imageData[arrayIndex] = bytes[i];
      }
    }
    console.log('After writing, imageData first 16 bytes:', imageData.slice(0, 16));
  }

  static async encryptData(data, password) {
    const key = await this.deriveKey(password);
    const iv = crypto.getRandomValues(new Uint8Array(16));

    const encrypted = await crypto.subtle.encrypt(
      { name: 'AES-CBC', iv },
      key,
      data
    );

    // Prepend IV
    const result = new Uint8Array(iv.length + encrypted.byteLength);
    result.set(iv, 0);
    result.set(new Uint8Array(encrypted), iv.length);
    return result;
  }

  static async decryptData(data, password) {
    const key = await this.deriveKey(password);
    const iv = data.slice(0, 16);
    const encryptedData = data.slice(16);

    const decrypted = await crypto.subtle.decrypt(
      { name: 'AES-CBC', iv },
      key,
      encryptedData
    );

    return new Uint8Array(decrypted);
  }

  static async deriveKey(password) {
    const keyMaterial = await crypto.subtle.importKey(
      'raw',
      new TextEncoder().encode(password),
      'PBKDF2',
      false,
      ['deriveKey']
    );

    return crypto.subtle.deriveKey(
      {
        name: 'PBKDF2',
        salt: new Uint8Array(16), // Fixed salt for compatibility
        iterations: 10000,
        hash: 'SHA-256'
      },
      keyMaterial,
      { name: 'AES-CBC', length: 256 },
      false,
      ['encrypt', 'decrypt']
    );
  }

  static arraysEqual(a, b) {
    if (a.length !== b.length) return false;
    return a.every((val, index) => val === b[index]);
  }
}

export default ClientImageProcessor;