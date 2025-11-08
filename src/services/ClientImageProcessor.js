// Cache-buster comment
/* global pako */

/**
 * Client-side Image Processor for Steganography
 * Supports two methods:
 * 1. Generate: Creates a new noisy image from the file data. High capacity, not subtle.
 * 2. LSB: Hides file data in the least significant bits of an existing carrier image. Low capacity, very subtle.
 */
class ClientImageProcessor {
  static SIGNATURE = "SC";
  static ENCODING_TYPE_GENERATED = 0;
  static ENCODING_TYPE_LSB = 1;

  static MAX_FILE_SIZE = 1024 * 1024 * 1024; // 1GB
  static BYTES_PER_PIXEL_GENERATED = 3; // RGB
  static SHA256_SIZE = 32;

  /**
   * Hides a file inside an existing carrier image using LSB steganography.
   * @param {File} payloadFile - The file to hide.
   * @param {File} carrierImageFile - The image to hide the file in.
   * @param {string|null} password - Optional password for encryption.
   * @param {number} bitDepth - The number of LSBs to use (1-4).
   * @param {function|null} onProgress - Progress callback.
   * @returns {Promise<Blob>} A new PNG blob with the hidden data.
   */
  static async hideInExistingImageAsync(payloadFile, carrierImageFile, password = null, bitDepth = 1, onProgress = null) {
    onProgress?.(5);
    const [rawPayloadData, carrierImg] = await Promise.all([
      this._readFileAsArrayBuffer(payloadFile),
      this._loadImage(carrierImageFile),
    ]);
    onProgress?.(10);

    // Compress the payload first
    const compressedPayload = pako.deflate(rawPayloadData);
    onProgress?.(15);

    const fileName = payloadFile.name;
    const isEncrypted = password != null;

    // Hash the ORIGINAL data, not the compressed or encrypted data
    const sha256Hash = await this._computeSHA256(rawPayloadData);
    onProgress?.(20);

    let processedData = compressedPayload;
    if (isEncrypted) {
      processedData = await this._encryptData(compressedPayload, password);
    }
    onProgress?.(30);

    const header = this._createHeader(processedData.byteLength, fileName, sha256Hash, isEncrypted, this.ENCODING_TYPE_LSB, true, bitDepth);
    const totalData = new Uint8Array(header.length + processedData.byteLength);
    totalData.set(header, 0);
    totalData.set(new Uint8Array(processedData), header.length);
    onProgress?.(40);

    const carrierCapacity = Math.floor((carrierImg.width * carrierImg.height * 3 * bitDepth) / 8);
    if (totalData.length > carrierCapacity) {
      throw new Error(`File is too large for the selected carrier image and bit depth. Required: ${totalData.length} bytes, Available: ${carrierCapacity} bytes.`);
    }
    onProgress?.(50);

    const canvas = document.createElement('canvas');
    canvas.width = carrierImg.width;
    canvas.height = carrierImg.height;
    const ctx = canvas.getContext('2d');
    ctx.drawImage(carrierImg, 0, 0);
    const imageData = ctx.getImageData(0, 0, carrierImg.width, carrierImg.height);
    onProgress?.(60);

    this._writeDataWithBitDepth(imageData.data, totalData, bitDepth);
    onProgress?.(80);

    ctx.putImageData(imageData, 0, 0);
    onProgress?.(90);

    return new Promise((resolve) => {
      canvas.toBlob((blob) => {
        onProgress?.(100);
        resolve(blob);
      }, 'image/png');
    });
  }

  /**
   * Creates a new carrier image from the file data (original method).
   */
  static async createCarrierImageAsync(file, password = null, onProgress = null) {
    const fileData = await this._readFileAsArrayBuffer(file);
    if (fileData.byteLength > this.MAX_FILE_SIZE) throw new Error('File too large.');
    onProgress?.(10);

    const sha256Hash = await this._computeSHA256(fileData);
    onProgress?.(20);

    let processedData = fileData;
    if (password) {
      processedData = await this._encryptData(fileData, password);
      onProgress?.(30);
    }

    const header = this._createHeader(processedData.byteLength, file.name, sha256Hash, !!password, this.ENCODING_TYPE_GENERATED, false, 0);
    onProgress?.(40);

    const totalData = new Uint8Array(header.length + processedData.byteLength);
    totalData.set(header, 0);
    totalData.set(new Uint8Array(processedData), header.length);
    onProgress?.(50);

    const pixelCount = Math.ceil(totalData.length / this.BYTES_PER_PIXEL_GENERATED);
    const imageSize = Math.ceil(Math.sqrt(pixelCount));
    onProgress?.(60);

    const canvas = document.createElement('canvas');
    canvas.width = imageSize;
    canvas.height = imageSize;
    const ctx = canvas.getContext('2d');
    ctx.fillStyle = 'white';
    ctx.fillRect(0, 0, imageSize, imageSize);

    const imageData = ctx.getImageData(0, 0, imageSize, imageSize);
    this._writeBytesDirectly(imageData.data, totalData);
    ctx.putImageData(imageData, 0, 0);
    onProgress?.(90);

    return new Promise((resolve) => {
      canvas.toBlob((blob) => {
        onProgress?.(100);
        resolve(blob);
      }, 'image/png');
    });
  }

  /**
   * Extracts a file from any supported carrier image.
   */
  static async extractFileAsync(imageFile, password = null, onProgress = null) {
    const img = await this._loadImage(imageFile);
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d');
    canvas.width = img.width;
    canvas.height = img.height;
    ctx.drawImage(img, 0, 0);

    try {
      const imageData = ctx.getImageData(0, 0, img.width, img.height);
      const pixelData = imageData.data;
      onProgress?.(20);

      const headerInfo = this._readHeader(pixelData);
      onProgress?.(40);

      let fileDataFromImage;
      if (headerInfo.encodingType === this.ENCODING_TYPE_LSB) {
        fileDataFromImage = this._readDataWithBitDepth(pixelData, headerInfo.totalHeaderSize, headerInfo.fileSize, headerInfo.bitDepth);
      } else {
        fileDataFromImage = this._readBytesDirectly(pixelData, headerInfo.totalHeaderSize, headerInfo.fileSize);
      }
      onProgress?.(60);

      let decryptedData = fileDataFromImage;
      if (headerInfo.isEncrypted) {
        if (!password) throw new Error('File is encrypted, but no password was provided.');
        try {
          decryptedData = await this._decryptData(fileDataFromImage, password);
        } catch (e) {
          throw new Error('Decryption failed. The password may be incorrect.');
        }
      }
      onProgress?.(70);

      let finalData = decryptedData;
      if (headerInfo.isCompressed) {
        finalData = pako.inflate(decryptedData);
      }
      onProgress?.(80);

      const computedHash = await this._computeSHA256(finalData);
      if (!this._arraysEqual(computedHash, headerInfo.sha256Hash)) {
        throw new Error('SHA256 hash mismatch. The file is likely corrupted.');
      }
      
      onProgress?.(100);
      return { fileName: headerInfo.fileName, data: finalData };
    } catch (error) {
      throw error;
    }
  }

  /**
   * Extracts metadata from a carrier image.
   */
  static async extractMetadataAsync(imageFile) {
    const img = await this._loadImage(imageFile);
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d');
    canvas.width = img.width;
    canvas.height = img.height;
    ctx.drawImage(img, 0, 0);
    const pixelData = ctx.getImageData(0, 0, img.width, img.height).data;
    return this._readHeader(pixelData);
  }

  // --- INTERNAL HELPERS ---

  static _createHeader(fileSize, fileName, sha256Hash, isEncrypted, encodingType, isCompressed, bitDepth) {
    const fileNameBytes = new TextEncoder().encode(fileName);
    if (fileNameBytes.length > 255) throw new Error('Filename too long');

    const header = new Uint8Array(512);
    let offset = 0;

    header.set(new TextEncoder().encode(this.SIGNATURE), offset);
    offset += 2;
    header[offset++] = encodingType;
    header[offset++] = isCompressed ? 1 : 0;
    header[offset++] = bitDepth;

    const sizeView = new DataView(new ArrayBuffer(8));
    // eslint-disable-next-line no-undef
    sizeView.setBigUint64(0, BigInt(fileSize), true);
    header.set(new Uint8Array(sizeView.buffer), offset);
    offset += 8;

    header[offset++] = fileNameBytes.length;
    header.set(fileNameBytes, offset);
    offset += fileNameBytes.length;

    header[offset++] = isEncrypted ? 1 : 0;

    header.set(sha256Hash, offset);
    offset += this.SHA256_SIZE;

    return header.slice(0, offset);
  }

  static _readHeader(pixelData) {
    // Try reading as LSB first, then fall back to direct reading
    let sigBytes, encodingType, isCompressed, bitDepth, signature;
    
    try {
      // First try reading with bit depth 1 (most common LSB)
      sigBytes = this._readDataWithBitDepth(pixelData, 0, 5, 1);
      signature = String.fromCharCode(sigBytes[0], sigBytes[1]);
      if (signature === this.SIGNATURE) {
        encodingType = sigBytes[2];
        isCompressed = sigBytes[3] === 1;
        bitDepth = sigBytes[4];
      } else {
        throw new Error('Not LSB encoded');
      }
    } catch {
      // Fall back to direct reading for generated images
      sigBytes = this._readBytesDirectly(pixelData, 0, 5);
      signature = String.fromCharCode(sigBytes[0], sigBytes[1]);
      if (signature !== this.SIGNATURE) throw new Error('Invalid signature. Not a valid carrier image.');
      
      encodingType = sigBytes[2];
      isCompressed = sigBytes[3] === 1;
      bitDepth = sigBytes[4];
    }

    let readFunc = (encodingType === this.ENCODING_TYPE_LSB) 
      ? (offset, len) => this._readDataWithBitDepth(pixelData, offset, len, bitDepth)
      : (offset, len) => this._readBytesDirectly(pixelData, offset, len);

    let offset = 5;
    const fileSizeData = readFunc(offset, 8);
    // eslint-disable-next-line no-undef
    const fileSize = new DataView(fileSizeData.buffer).getBigUint64(0, true);
    offset += 8;

    const fileNameLengthData = readFunc(offset, 1);
    const fileNameLength = fileNameLengthData[0];
    offset += 1;

    const fileNameBytes = readFunc(offset, fileNameLength);
    const fileName = new TextDecoder().decode(fileNameBytes);
    offset += fileNameLength;

    const isEncryptedData = readFunc(offset, 1);
    const isEncrypted = isEncryptedData[0] === 1;
    offset += 1;

    const sha256Hash = readFunc(offset, this.SHA256_SIZE);
    offset += this.SHA256_SIZE;

    return {
      signature, encodingType, isCompressed, bitDepth,
      fileSize: Number(fileSize),
      fileName, isEncrypted, sha256Hash,
      totalHeaderSize: offset
    };
  }

  static _writeBytesDirectly(imageData, bytes) {
    let byteIndex = 0;
    for (let i = 0; i < imageData.length && byteIndex < bytes.length; i += 4) {
      imageData[i] = bytes[byteIndex++];
      if (byteIndex < bytes.length) imageData[i + 1] = bytes[byteIndex++];
      if (byteIndex < bytes.length) imageData[i + 2] = bytes[byteIndex++];
    }
  }

  static _readBytesDirectly(pixelData, startOffset, length) {
    const bytes = new Uint8Array(length);
    for (let i = 0; i < length; i++) {
      const dataBytePosition = startOffset + i;
      const pixel = Math.floor(dataBytePosition / 3);
      const channel = dataBytePosition % 3;
      bytes[i] = pixelData[pixel * 4 + channel];
    }
    return bytes;
  }

  static _writeDataWithBitDepth(imageData, bytes, bitDepth) {
    let dataBitIndex = 0;
    let dataByte = bytes[0];
    let dataByteBitIndex = 0;

    for (let i = 0; i < imageData.length; i++) {
      if (i % 4 === 3) continue; // Skip alpha channel

      if (dataBitIndex >= bytes.length * 8) break;

      let bitsToStore = 0;
      for (let j = 0; j < bitDepth; j++) {
        if (dataBitIndex >= bytes.length * 8) break;
        
        const bit = (dataByte >> (7 - dataByteBitIndex)) & 1;
        bitsToStore = (bitsToStore << 1) | bit;

        dataBitIndex++;
        dataByteBitIndex++;
        if (dataByteBitIndex === 8) {
          dataByteBitIndex = 0;
          dataByte = bytes[Math.floor(dataBitIndex / 8)];
        }
      }
      
      const originalChannelValue = imageData[i];
      const clearedChannelValue = originalChannelValue & (0xFF << bitDepth);
      imageData[i] = clearedChannelValue | bitsToStore;
    }
  }

  static _readDataWithBitDepth(pixelData, startOffset, length, bitDepth) {
    const bytes = new Uint8Array(length);
    // eslint-disable-next-line no-unused-vars
    const mask = (1 << bitDepth) - 1;
    let bitIndex = startOffset * 8;

    for (let i = 0; i < length; i++) {
      let currentByte = 0;
      for (let j = 0; j < 8; j += bitDepth) {
        if (bitIndex >= pixelData.length * 8) break;
        const pixelIndex = Math.floor(bitIndex / (3 * bitDepth));
        const channelOffset = Math.floor((bitIndex % (3 * bitDepth)) / bitDepth);
        const channelIndex = pixelIndex * 4 + channelOffset;
        const bits = pixelData[channelIndex] & mask;
        currentByte = (currentByte << bitDepth) | bits;
        bitIndex += bitDepth;
      }
      bytes[i] = currentByte;
    }
    return bytes;
  }

  static async _readFileAsArrayBuffer(file) {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result);
      reader.onerror = () => reject(new Error('Failed to read file'));
      reader.readAsArrayBuffer(file);
    });
  }

  static async _loadImage(imageFile) {
    return new Promise((resolve, reject) => {
      const img = new Image();
      img.onload = () => resolve(img);
      img.onerror = () => reject(new Error('Failed to load image file.'));
      img.src = URL.createObjectURL(imageFile);
    });
  }

  static async _computeSHA256(data) {
    const hashBuffer = await crypto.subtle.digest('SHA-256', data);
    return new Uint8Array(hashBuffer);
  }

  static async _encryptData(data, password) {
    const key = await this._deriveKey(password);
    const iv = crypto.getRandomValues(new Uint8Array(16));
    const encrypted = await crypto.subtle.encrypt({ name: 'AES-CBC', iv }, key, data);
    const result = new Uint8Array(iv.length + encrypted.byteLength);
    result.set(iv, 0);
    result.set(new Uint8Array(encrypted), iv.length);
    return result;
  }

  static async _decryptData(data, password) {
    const key = await this._deriveKey(password);
    const iv = data.slice(0, 16);
    const encryptedData = data.slice(16);
    const decrypted = await crypto.subtle.decrypt({ name: 'AES-CBC', iv }, key, encryptedData);
    return new Uint8Array(decrypted);
  }

  static async _deriveKey(password) {
    const keyMaterial = await crypto.subtle.importKey('raw', new TextEncoder().encode(password), 'PBKDF2', false, ['deriveKey']);
    return crypto.subtle.deriveKey(
      { name: 'PBKDF2', salt: new Uint8Array(16), iterations: 10000, hash: 'SHA-256' },
      keyMaterial,
      { name: 'AES-CBC', length: 256 },
      false,
      ['encrypt', 'decrypt']
    );
  }

  static _arraysEqual(a, b) {
    if (a.length !== b.length) return false;
    return a.every((val, index) => val === b[index]);
  }
}

export default ClientImageProcessor;