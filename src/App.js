import React, { useState, useEffect } from 'react';
import { Lock, Unlock, Upload, FileImage, CheckCircle, XCircle, Info, KeyRound, ScanEye } from 'lucide-react';
import MatrixRain from './components/MatrixRain';
import TypeWriter from './components/TypeWriter';
import ClientImageProcessor from './services/ClientImageProcessor';
import './App.css';

function App() {
  const [mode, setMode] = useState('crypt'); // 'crypt' or 'decrypt'
  const [file, setFile] = useState(null);
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [progress, setProgress] = useState(0);
  const [result, setResult] = useState(null);
  const [showIntro, setShowIntro] = useState(true);
  const [dragActive, setDragActive] = useState(false);
  const [metadata, setMetadata] = useState(null);
  const [metadataError, setMetadataError] = useState(null);
  const [shakePassword, setShakePassword] = useState(false);

  useEffect(() => {
    const timer = setTimeout(() => setShowIntro(false), 2000);
    return () => clearTimeout(timer);
  }, []);

  // Effect to extract metadata when a file is selected in decrypt mode
  useEffect(() => {
    setMetadata(null);
    setMetadataError(null);
    if (mode === 'decrypt' && file) {
      const getMetadata = async () => {
        try {
          const meta = await ClientImageProcessor.extractMetadataAsync(file);
          setMetadata(meta);
        } catch (error) {
          setMetadataError(error.message);
        }
      };
      getMetadata();
    }
  }, [file, mode]);

  const handleFileChangeAndDrop = (selectedFile) => {
    if (!selectedFile) return;
    // File type validation for decrypt mode
    if (mode === 'decrypt' && !selectedFile.type.includes('png')) {
      setResult({ success: false, message: 'Please select a PNG file for decryption.' });
      return;
    }
    setFile(selectedFile);
    setResult(null);
    setPassword('');
  };

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
      handleFileChangeAndDrop(e.dataTransfer.files[0]);
    }
  };

  const handleFileChange = (e) => {
    if (e.target.files && e.target.files[0]) {
      handleFileChangeAndDrop(e.target.files[0]);
    }
  };
  
  const triggerPasswordShake = () => {
    setShakePassword(true);
    setTimeout(() => setShakePassword(false), 500);
  };

  const handleProcess = async () => {
    if (!file) return;

    setLoading(true);
    setProgress(0);
    setResult(null);

    const usePassword = password.length > 0 ? password : null;

    try {
      if (mode === 'crypt') {
        const pngBlob = await ClientImageProcessor.createCarrierImageAsync(
          file,
          usePassword,
          (p) => setProgress(p)
        );
        
        const baseName = file.name.lastIndexOf('.') > 0 ? file.name.substring(0, file.name.lastIndexOf('.')) : file.name;
        const filename = `${baseName}-encrypted.png`;
        
        const url = window.URL.createObjectURL(pngBlob);
        const link = document.createElement('a');
        link.href = url;
        link.setAttribute('download', filename);
        document.body.appendChild(link);
        link.click();
        link.remove();
        setResult({ success: true, filename: filename, size: `${(pngBlob.size / 1024 / 1024).toFixed(2)} MB` });
      } else {
        const extracted = await ClientImageProcessor.extractFileAsync(
          file,
          usePassword,
          (p) => setProgress(p)
        );
        const blob = new Blob([extracted.data]);
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;

        const originalName = extracted.fileName;
        const lastDotIndex = originalName.lastIndexOf('.');
        let decryptedFilename;

        if (lastDotIndex > 0) {
          const baseName = originalName.substring(0, lastDotIndex);
          const extension = originalName.substring(lastDotIndex);
          decryptedFilename = `${baseName}-decrypted${extension}`;
        } else {
          decryptedFilename = `${originalName}-decrypted`;
        }

        link.setAttribute('download', decryptedFilename);
        document.body.appendChild(link);
        link.click();
        link.remove();
        setResult({ success: true, filename: decryptedFilename, size: `${(blob.size / 1024 / 1024).toFixed(2)} MB` });
      }
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

  return (
    <div className="min-h-screen bg-black text-green-400 font-mono relative overflow-hidden">
      <MatrixRain />
      
      {showIntro && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black p-4">
          <div className="text-center">
            <h1 className="text-4xl sm:text-5xl md:text-6xl font-bold mb-4 text-green-400">
               <TypeWriter text="SHADE_OF_COLOR_2" delay={80} />
             </h1>
             <p className="text-lg sm:text-xl text-green-500">
               <TypeWriter text="> Steganography System Online..." delay={50} />
             </p>
          </div>
        </div>
      )}

      <div className="relative z-10 min-h-screen p-4 md:p-8">
        <header className="mb-8 sm:mb-12 text-center">
          <div className="inline-block">
            <h1 className="text-3xl sm:text-4xl md:text-5xl font-bold mb-2 text-transparent bg-clip-text bg-gradient-to-r from-green-400 to-white drop-shadow-[0_0_20px_rgba(0,255,65,0.5)]">
              SHADE_OF_COLOR_2
            </h1>
             <p className="text-sm text-green-500 tracking-widest">
               &gt; FILE STEGANOGRAPHY SYSTEM v2.2 (ENCRYPTION ENABLED)
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

            <div onDragEnter={handleDrag} onDragLeave={handleDrag} onDragOver={handleDrag} onDrop={handleDrop} className={`relative border-2 border-dashed rounded-xl p-6 sm:p-8 md:p-12 mb-6 transition-all duration-300 ${ dragActive ? 'border-green-400 bg-green-500/10 shadow-[0_0_30px_rgba(0,255,65,0.3)]' : 'border-green-500/40 hover:border-green-400/60 hover:bg-green-500/5' }`}>
              <input type="file" onChange={handleFileChange} className="absolute inset-0 w-full h-full opacity-0 cursor-pointer" accept={mode === 'decrypt' ? '.png' : '*'} />
              <div className="text-center">
                {file ? (
                  <>
                    <FileImage className="w-12 h-12 sm:w-16 sm:h-16 mx-auto mb-4 text-green-400 animate-bounce" />
                    <p className="text-base sm:text-lg text-green-400 mb-2 font-semibold truncate" title={file.name}>{file.name.length > 30 ? `${file.name.substring(0, 27)}...` : file.name}</p>
                    <p className="text-xs sm:text-sm text-green-600">{(file.size / 1024 / 1024).toFixed(2)} MB • {file.type || 'Unknown type'}</p>
                    {mode === 'decrypt' && (
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
                ) : (
                  <>
                    <Upload className="w-12 h-12 sm:w-16 sm:h-16 mx-auto mb-4 text-green-500/50 animate-pulse" />
                    <p className="text-base sm:text-lg text-green-500 mb-2">{mode === 'crypt' ? 'Drop file to hide in PNG' : 'Drop PNG to extract file'}</p>
                    <p className="text-sm text-green-700">or click to browse</p>
                    <p className="text-xs text-green-800 mt-2">Processing is done in your browser. Your files are never uploaded.</p>
                  </>
                )}
              </div>
            </div>

            {file && (
              <div className="space-y-4">
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
                      {loading ? (
                        <><span className="text-sm font-mono">{progress}%</span><span>PROCESSING...</span></>
                      ) : (
                        <>{mode === 'crypt' ? <Lock className="w-5 h-5" /> : <Unlock className="w-5 h-5" />}{mode === 'crypt' ? 'HIDE IN PNG' : 'EXTRACT FROM PNG'}</>
                      )}
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

          <div className="mt-8 backdrop-blur-xl bg-black/20 border border-green-500/20 rounded-lg p-4 sm:p-6">
            <div className="flex items-start gap-3">
              <Info className="w-5 h-5 text-green-600 flex-shrink-0 mt-0.5" />
              <div className="text-sm text-green-700 space-y-2">
                <p>&gt; Hide any file inside a PNG image using steganography</p>
                <p>&gt; Optional AES-256 encryption for enhanced security</p>
                <p>&gt; All processing is done 100% in your browser. Files are never uploaded.</p>
              </div>
            </div>
          </div>
        </div>

        <footer className="text-center mt-12 text-green-600 text-sm space-x-4">
          <a href="https://github.com/archistico/ShadeOfColor2" target="_blank" rel="noopener noreferrer" className="hover:text-green-400 hover:underline">&gt; Original by @archistico</a>
          <span>|</span>
          <a href="https://github.com/FettahFT/SOC2-web" target="_blank" rel="noopener noreferrer" className="hover:text-green-400 hover:underline">Fork by @FettahFT</a>
        </footer>
      </div>
    </div>
  );
}

export default App;