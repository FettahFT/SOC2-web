import React, { useState, useEffect } from 'react';
import { Lock, Unlock, Upload, FileImage, CheckCircle, XCircle, KeyRound, ScanEye, Server, Image as ImageIcon } from 'lucide-react';
// eslint-disable-next-line no-unused-vars
import { Info } from 'lucide-react';
import MatrixRain from './components/MatrixRain';
// eslint-disable-next-line no-unused-vars
import TypeWriter from './components/TypeWriter';
import ClientImageProcessor from './services/ClientImageProcessor';
import './App.css';

// A new component for the file dropzone to reduce repetition
const FileDropzone = ({ onDrop, file, title, subtitle, accept, error }) => {
  const [dragActive, setDragActive] = useState(false);

  const handleDrag = (e) => {
    e.preventDefault();
    e.stopPropagation();
    if (e.type === 'dragenter' || e.type === 'dragover') setDragActive(true);
    else if (e.type === 'dragleave') setDragActive(false);
  };

  const handleDrop = (e) => {
    e.preventDefault();
    e.stopPropagation();
    setDragActive(false);
    if (e.dataTransfer.files && e.dataTransfer.files[0]) {
      onDrop(e.dataTransfer.files[0]);
    }
  };

  const handleFileChange = (e) => {
    if (e.target.files && e.target.files[0]) {
      onDrop(e.target.files[0]);
    }
  };

  return (
    <div
      onDragEnter={handleDrag}
      onDragLeave={handleDrag}
      onDragOver={handleDrag}
      onDrop={handleDrop}
      className={`relative border-2 border-dashed rounded-xl p-6 text-center transition-all duration-300 ${
        dragActive
          ? 'border-green-400 bg-green-500/10 shadow-[0_0_30px_rgba(0,255,65,0.3)]'
          : 'border-green-500/40 hover:border-green-400/60 hover:bg-green-500/5'
      } ${error ? 'border-red-500/60' : ''}`}
    >
      <input type="file" onChange={handleFileChange} className="absolute inset-0 w-full h-full opacity-0 cursor-pointer" accept={accept} />
      {file ? (
        <>
          <FileImage className="w-10 h-10 mx-auto mb-2 text-green-400" />
          <p className="text-sm text-green-400 font-semibold truncate" title={file.name}>{file.name}</p>
          <p className="text-xs text-green-600">{(file.size / 1024 / 1024).toFixed(2)} MB</p>
        </>
      ) : (
        <>
          <Upload className="w-10 h-10 mx-auto mb-2 text-green-500/50" />
          <p className="text-base text-green-500 mb-1">{title}</p>
          <p className="text-sm text-green-700">{subtitle}</p>
        </>
      )}
      {error && <p className="text-xs text-red-400 mt-2">{error}</p>}
    </div>
  );
};


function App() {
  const [mode, setMode] = useState('crypt'); // 'crypt' or 'decrypt'
  const [stegoMode, setStegoMode] = useState('generate'); // 'generate' or 'lsb'
  
  const [payloadFile, setPayloadFile] = useState(null);
  const [carrierFile, setCarrierFile] = useState(null);
  
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [progress, setProgress] = useState(0);
  const [result, setResult] = useState(null);
  const [shakePassword, setShakePassword] = useState(false);
  
  const [metadata, setMetadata] = useState(null);
  const [metadataError, setMetadataError] = useState(null);
  const [carrierCapacity, setCarrierCapacity] = useState(null);
  const [carrierError, setCarrierError] = useState(null);

  // Reset inputs when mode changes
  useEffect(() => {
    setPayloadFile(null);
    setCarrierFile(null);
    setResult(null);
    setPassword('');
    setMetadata(null);
    setMetadataError(null);
    setCarrierCapacity(null);
    setCarrierError(null);
  }, [mode, stegoMode]);

  // Effect for metadata extraction in 'decrypt' mode
  useEffect(() => {
    setMetadata(null);
    setMetadataError(null);
    if (mode === 'decrypt' && payloadFile) {
      ClientImageProcessor.extractMetadataAsync(payloadFile)
        .then(setMetadata)
        .catch(err => setMetadataError(err.message));
    }
  }, [payloadFile, mode]);

  // Effect for calculating carrier capacity in LSB mode
  useEffect(() => {
    setCarrierCapacity(null);
    setCarrierError(null);
    if (mode === 'crypt' && stegoMode === 'lsb' && carrierFile) {
      const img = new Image();
      img.onload = () => {
        const capacity = Math.floor((img.width * img.height * 3) / 8);
        setCarrierCapacity(capacity);
      };
      img.onerror = () => {
        setCarrierError('Could not read carrier image.');
      };
      img.src = URL.createObjectURL(carrierFile);
    }
  }, [carrierFile, mode, stegoMode]);

  const triggerPasswordShake = () => {
    setShakePassword(true);
    setTimeout(() => setShakePassword(false), 500);
  };

  const handleProcess = async () => {
    if ((mode === 'crypt' && !payloadFile) || (mode === 'decrypt' && !payloadFile)) return;
    if (mode === 'crypt' && stegoMode === 'lsb' && !carrierFile) return;

    setLoading(true);
    setProgress(0);
    setResult(null);

    const usePassword = password.length > 0 ? password : null;

    try {
      let blob, outputFilename;

      if (mode === 'crypt') {
        if (stegoMode === 'generate') {
          blob = await ClientImageProcessor.createCarrierImageAsync(payloadFile, usePassword, setProgress);
          const baseName = payloadFile.name.lastIndexOf('.') > 0 ? payloadFile.name.substring(0, payloadFile.name.lastIndexOf('.')) : payloadFile.name;
          outputFilename = `${baseName}-generated.png`;
        } else { // lsb
          blob = await ClientImageProcessor.hideInExistingImageAsync(payloadFile, carrierFile, usePassword, setProgress);
          const baseName = carrierFile.name.lastIndexOf('.') > 0 ? carrierFile.name.substring(0, carrierFile.name.lastIndexOf('.')) : carrierFile.name;
          outputFilename = `${baseName}-lsb-encoded.png`;
        }
      } else { // decrypt
        const extracted = await ClientImageProcessor.extractFileAsync(payloadFile, usePassword, setProgress);
        blob = new Blob([extracted.data]);
        const originalName = extracted.fileName;
        const lastDotIndex = originalName.lastIndexOf('.');
        if (lastDotIndex > 0) {
          const baseName = originalName.substring(0, lastDotIndex);
          const extension = originalName.substring(lastDotIndex);
          outputFilename = `${baseName}-decrypted${extension}`;
        } else {
          outputFilename = `${originalName}-decrypted`;
        }
      }

      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.setAttribute('download', outputFilename);
      document.body.appendChild(link);
      link.click();
      link.remove();
      setResult({ success: true, filename: outputFilename, size: `${(blob.size / 1024 / 1024).toFixed(2)} MB` });

    } catch (error) {
      if (mode === 'decrypt' && error.message.includes('Decryption failed')) {
        setResult({ success: false, message: 'Incorrect Password. Please try again.' });
        triggerPasswordShake();
      } else {
        setResult({ success: false, message: error.message });
      }
    } finally {
      setLoading(false);
      setProgress(100);
    }
  };

  const renderCryptMode = () => {
    return (
      <>
        {/* Stego Mode Selector */}
        <div className="grid grid-cols-2 gap-2 mb-4">
          <button onClick={() => setStegoMode('generate')} className={`p-3 rounded-md text-sm transition-all ${stegoMode === 'generate' ? 'bg-green-500/20 border border-green-400 text-white' : 'bg-black/20 border border-transparent hover:border-green-700'}`}>
            <div className="flex items-center justify-center gap-2"><Server className="w-5 h-5" /><span>Generate New Image</span></div>
          </button>
          <button onClick={() => setStegoMode('lsb')} className={`p-3 rounded-md text-sm transition-all ${stegoMode === 'lsb' ? 'bg-green-500/20 border border-green-400 text-white' : 'bg-black/20 border border-transparent hover:border-green-700'}`}>
            <div className="flex items-center justify-center gap-2"><ImageIcon className="w-5 h-5" /><span>Hide in Existing Image</span></div>
          </button>
        </div>

        {stegoMode === 'generate' ? (
          <FileDropzone onDrop={setPayloadFile} file={payloadFile} title="Drop Secret File Here" subtitle="or click to browse" />
        ) : (
          <div className="grid md:grid-cols-2 gap-4">
            <FileDropzone onDrop={setPayloadFile} file={payloadFile} title="1. Drop Secret File" subtitle="The file to hide" />
            <div>
              <FileDropzone onDrop={setCarrierFile} file={carrierFile} title="2. Drop Carrier Image" subtitle="The image to hide in" accept="image/png" error={carrierError} />
              {carrierCapacity && (
                <div className="text-xs text-center mt-2 text-green-600">
                  Capacity: {(carrierCapacity / 1024).toFixed(2)} KB
                </div>
              )}
            </div>
          </div>
        )}
      </>
    );
  };

  const renderDecryptMode = () => (
    <>
      <FileDropzone onDrop={setPayloadFile} file={payloadFile} title="Drop PNG to Extract From" subtitle="or click to browse" accept="image/png" />
      {mode === 'decrypt' && payloadFile && (
        <div className="mt-4 pt-4 border-t border-green-500/20 text-left">
          {metadata ? (
            <div className="space-y-2 text-sm">
              <h4 className="flex items-center gap-2 text-green-400 font-bold mb-2"><ScanEye className="w-5 h-5"/> Embedded File Info:</h4>
              <div className="flex justify-between"><span className="text-green-600">Filename:</span><span className="text-white truncate ml-4" title={metadata.fileName}>{metadata.fileName}</span></div>
              <div className="flex justify-between"><span className="text-green-600">Size:</span><span className="text-white">{(metadata.fileSize / 1024).toFixed(2)} KB</span></div>
              <div className="flex justify-between"><span className="text-green-600">Encrypted:</span><span className="text-white">{metadata.isEncrypted ? 'Yes' : 'No'}</span></div>
            </div>
          ) : metadataError ? (
            <p className="text-sm text-red-400">{metadataError}</p>
          ) : (
            <p className="text-sm text-green-600 animate-pulse">Scanning for metadata...</p>
          )}
        </div>
      )}
    </>
  );

  return (
    <div className="min-h-screen bg-black text-green-400 font-mono relative overflow-hidden">
      <MatrixRain />
      <div className="relative z-10 min-h-screen p-4 md:p-8">
        <header className="mb-8 sm:mb-12 text-center">
          <div className="inline-block">
            <h1 className="text-3xl sm:text-4xl md:text-5xl font-bold mb-2 text-transparent bg-clip-text bg-gradient-to-r from-green-400 to-white drop-shadow-[0_0_20px_rgba(0,255,65,0.5)]">
              SHADE_OF_COLOR_2
            </h1>
             <p className="text-sm text-green-500 tracking-widest">
               &gt; STEGANOGRAPHY SYSTEM v2.3 (DUAL MODE)
             </p>
          </div>
        </header>

        <div className="max-w-2xl mx-auto mb-8">
          <div className="backdrop-blur-xl bg-black/30 border border-green-500/30 rounded-lg p-1 shadow-[0_0_30px_rgba(0,255,65,0.15)]">
            <div className="mode-selector-container grid grid-cols-2 gap-1 sm:gap-2">
              <div className={`moving-border ${mode === 'decrypt' ? 'decrypt' : ''}`}></div>
               <button onClick={() => setMode('crypt')} className="mode-selector-button flex items-center justify-center gap-2 py-3 px-4 sm:py-4 sm:px-6 text-green-400" style={{ outline: 'none', boxShadow: 'none', border: 'none', backgroundColor: 'transparent' }}>
                 <Lock className="w-5 h-5" />
                 <span className="font-semibold tracking-wider text-sm sm:text-base">HIDE</span>
               </button>
               <button onClick={() => setMode('decrypt')} className="mode-selector-button flex items-center justify-center gap-2 py-3 px-4 sm:py-4 sm:px-6 text-green-400" style={{ outline: 'none', boxShadow: 'none', border: 'none', backgroundColor: 'transparent' }}>
                 <Unlock className="w-5 h-5" />
                 <span className="font-semibold tracking-wider text-sm sm:text-base">EXTRACT</span>
               </button>
            </div>
          </div>
        </div>

        <div className="max-w-4xl mx-auto">
          <div className="backdrop-blur-2xl bg-gradient-to-br from-black/50 to-green-950/10 border border-green-500/30 rounded-2xl p-4 sm:p-6 md:p-8 shadow-[0_0_50px_rgba(0,255,65,0.2)]">
            
            {mode === 'crypt' ? renderCryptMode() : renderDecryptMode()}

            {payloadFile && (
              <div className="mt-6 space-y-4">
                <div className="relative">
                  <KeyRound className="absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-green-700" />
                  <input
                    type="password"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    placeholder="Enter Password (Optional)"
                    className={`w-full bg-black/30 border border-green-500/30 rounded-lg py-3 pr-4 pl-10 text-green-300 placeholder-green-700 focus:ring-1 focus:ring-green-500 ${shakePassword ? 'shake' : ''}`}
                  />
                </div>

                <div className="relative">
                  <button onClick={handleProcess} disabled={loading} className={`w-full py-4 px-6 font-bold rounded-lg transition-all duration-300 tracking-wider relative overflow-hidden ${ loading ? 'bg-green-950/20 text-green-400 border border-green-500/40' : 'bg-gradient-to-r from-green-600 to-green-500 hover:from-green-500 hover:to-green-400 text-black shadow-[0_0_30px_rgba(0,255,65,0.4)] hover:shadow-[0_0_50px_rgba(0,255,65,0.6)]' }`}>
                    <span className="relative z-10 flex items-center justify-center gap-3">
                      {loading ? <><span className="text-sm font-mono">{progress}%</span><span>PROCESSING...</span></> : <>{mode === 'crypt' ? <Lock className="w-5 h-5" /> : <Unlock className="w-5 h-5" />}{mode === 'crypt' ? 'HIDE FILE' : 'EXTRACT FILE'}</>}
                    </span>
                    {loading && (<div className="absolute inset-0 bg-gradient-to-r from-green-500 to-green-400 opacity-40 transition-all duration-500 ease-out rounded-lg" style={{ width: `${progress}%` }}/>)}
                  </button>
                </div>
              </div>
            )}

            {result && (
              <div className={`mt-6 backdrop-blur-xl border rounded-lg p-4 sm:p-6 shadow-[0_0_20px_rgba(0,255,65,0.15)] transition-all duration-500 animate-fade-in ${ result.success ? 'bg-green-950/20 border-green-500/40' : 'bg-red-950/20 border-red-500/40' }`}>
                <div className="flex items-start gap-4">
                  {result.success ? <CheckCircle className="w-6 h-6 text-green-400 flex-shrink-0 mt-1" /> : <XCircle className="w-6 h-6 text-red-400 flex-shrink-0 mt-1" />}
                  <div className="flex-1">
                    <h3 className="text-lg font-semibold text-green-400 mb-3">&gt; {result.success ? 'SUCCESS' : 'ERROR'}</h3>
                    {result.success ? (
                      <div className="space-y-2 text-sm">
                        <div className="flex justify-between"><span className="text-green-600">Output:</span><span className="text-white">{result.filename}</span></div>
                        <div className="flex justify-between"><span className="text-green-600">Size:</span><span className="text-white">{result.size}</span></div>
                      </div>
                    ) : (
                      <p className="text-red-400">{result.message}</p>
                    )}
                  </div>
                </div>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

export default App;