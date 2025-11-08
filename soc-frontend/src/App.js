import React, { useState, useEffect } from 'react';
import axios from 'axios';
import { Lock, Unlock, Upload, FileImage, CheckCircle, XCircle, Info } from 'lucide-react';
import MatrixRain from './components/MatrixRain';
import TypeWriter from './components/TypeWriter';
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


  const API_URL = process.env.REACT_APP_API_URL || 'http://localhost:5046';



  useEffect(() => {
    const timer = setTimeout(() => setShowIntro(false), 2000);
    return () => clearTimeout(timer);
  }, []);

  const handleDrag = (e) => {
    e.preventDefault();
    e.stopPropagation();
    if (e.type === 'dragenter' || e.type === 'dragover') {
      setDragActive(true);
    } else if (e.type === 'dragleave') {
      setDragActive(false);
    }
  };

  const handleDrop = (e) => {
    e.preventDefault();
    e.stopPropagation();
    setDragActive(false);
    if (e.dataTransfer.files && e.dataTransfer.files[0]) {
      const selectedFile = e.dataTransfer.files[0];

      // File size validation (50MB limit)
      if (selectedFile.size > 50 * 1024 * 1024) {
        setResult({ success: false, message: 'File too large. Maximum size is 50MB.' });
        return;
      }

      // File type validation for decrypt mode
      if (mode === 'decrypt' && !selectedFile.type.includes('png')) {
        setResult({ success: false, message: 'Please select a PNG file for decryption.' });
        return;
      }

      setFile(selectedFile);
      setResult(null);
    }
  };

  const handleFileChange = (e) => {
    if (e.target.files && e.target.files[0]) {
      const selectedFile = e.target.files[0];

      // File size validation (50MB limit)
      if (selectedFile.size > 50 * 1024 * 1024) {
        setResult({ success: false, message: 'File too large. Maximum size is 50MB.' });
        return;
      }

      // File type validation for decrypt mode
      if (mode === 'decrypt' && !selectedFile.type.includes('png')) {
        setResult({ success: false, message: 'Please select a PNG file for decryption.' });
        return;
      }

      setFile(selectedFile);
      setResult(null);
    }
  };

  const handleProcess = async () => {
    if (!file) return;

    // Require password for all operations
    if (!password.trim()) {
      setResult({ success: false, message: 'Password is required for all operations.' });
      return;
    }

    setLoading(true);
    setProgress(0);
    setResult(null);

    // Simulate progress
    const progressInterval = setInterval(() => {
      setProgress(prev => Math.min(prev + 10, 90));
    }, 500);

    const formData = new FormData();
    formData.append(mode === 'crypt' ? 'file' : 'image', file);
    formData.append('password', password.trim());

    try {
      const endpoint = mode === 'crypt' ? '/api/hide' : '/api/extract';
      const response = await axios.post(`${API_URL}${endpoint}`, formData, {
        responseType: 'blob',
      });

      const url = window.URL.createObjectURL(new Blob([response.data]));
      const link = document.createElement('a');
      link.href = url;
      
      const filename = mode === 'crypt' 
        ? `image_${Math.random().toString(36).substring(2, 10)}.png`
        : response.headers['x-original-filename'] || 'extracted_file';
      
      link.setAttribute('download', filename);
      document.body.appendChild(link);
      link.click();
      link.remove();
      
      setProgress(100);
      setResult({
        success: true,
        filename: filename,
        size: `${(response.data.size / 1024 / 1024).toFixed(2)} MB`
      });
    } catch (error) {
      setProgress(100);
      let errorMessage = 'An unexpected error occurred';

      if (error.response) {
        const status = error.response.status;

        // Try to get error message from response
        if (error.response.data) {
          if (error.response.data instanceof Blob) {
            try {
              const text = await error.response.data.text();
              if (text) {
                try {
                  const parsed = JSON.parse(text);
                  errorMessage = parsed.error || parsed.message || text;
                } catch {
                  errorMessage = text;
                }
              } else {
                errorMessage = `Server error (${status}) - no details provided`;
              }
            } catch {
              errorMessage = `Server error (${status}) - unable to read response`;
            }
          } else if (typeof error.response.data === 'object' && error.response.data.error) {
            errorMessage = error.response.data.error;
          } else if (typeof error.response.data === 'string') {
            errorMessage = error.response.data;
          } else {
            errorMessage = `Server error (${status})`;
          }
        } else {
          errorMessage = `Server error (${status}) - empty response`;
        }
      } else if (error.request) {
        errorMessage = 'Network error - unable to reach server. Check your internet connection.';
      } else {
        errorMessage = error.message || 'Unknown error occurred';
      }

      setResult({
        success: false,
        message: errorMessage
      });
    } finally {
      setLoading(false);
      clearInterval(progressInterval);
    }
  };



  return (
    <div className="min-h-screen bg-black text-green-400 font-mono relative overflow-hidden">
      <MatrixRain />
      
      {showIntro && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black">
          <div className="text-center">
            <h1 className="text-6xl font-bold mb-4 text-green-400">
              <TypeWriter text="SHADE_OF_COLOR_2" delay={80} />
            </h1>
            <p className="text-xl text-green-500">
              <TypeWriter text="> Steganography Protocol Initialized..." delay={50} />
            </p>
          </div>
        </div>
      )}

      <div className="relative z-10 min-h-screen p-8">
        {/* Header */}
        <header className="mb-12 text-center">
          <div className="inline-block">
            <h1 className="text-5xl font-bold mb-2 text-transparent bg-clip-text bg-gradient-to-r from-green-400 to-white drop-shadow-[0_0_20px_rgba(0,255,65,0.5)]">
              SHADE_OF_COLOR_2
            </h1>
            <p className="text-sm text-green-500 tracking-widest">
              &gt; FILE STEGANOGRAPHY SYSTEM v2.0
            </p>
          </div>
        </header>

        {/* Mode Selector */}
        <div className="max-w-2xl mx-auto mb-8">
          <div className="backdrop-blur-xl bg-black/30 border border-green-500/30 rounded-lg p-1 shadow-[0_0_30px_rgba(0,255,65,0.15)]">
            <div className="mode-selector-container grid grid-cols-2 gap-2">
              <div className={`moving-border ${mode === 'decrypt' ? 'decrypt' : ''}`}></div>
              <button
                onClick={() => setMode('crypt')}
                className="mode-selector-button flex items-center justify-center gap-2 py-4 px-6 text-green-400"
                style={{ outline: 'none', boxShadow: 'none', border: 'none', backgroundColor: 'transparent' }}
              >
                <Lock className="w-5 h-5" />
                <span className="font-semibold tracking-wider">ENCRYPT</span>
              </button>
              <button
                onClick={() => setMode('decrypt')}
                className="mode-selector-button flex items-center justify-center gap-2 py-4 px-6 text-green-400"
                style={{ outline: 'none', boxShadow: 'none', border: 'none', backgroundColor: 'transparent' }}
              >
                <Unlock className="w-5 h-5" />
                <span className="font-semibold tracking-wider">DECRYPT</span>
              </button>
            </div>
          </div>
        </div>

        {/* Main Interface */}
        <div className="max-w-4xl mx-auto">
          <div className="backdrop-blur-2xl bg-gradient-to-br from-black/50 to-green-950/10 border border-green-500/30 rounded-2xl p-8 shadow-[0_0_50px_rgba(0,255,65,0.2)]">
            {/* Password Input */}
            <div className="mb-6">
              <label htmlFor="password" className="block text-sm font-semibold text-green-400 mb-2">
                Password *
              </label>
              <input
                type="password"
                id="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Enter password for encryption/decryption"
                required
                className="w-full px-4 py-3 bg-black/50 border border-green-500/40 rounded-lg text-green-400 placeholder-green-600 focus:outline-none focus:border-green-400 focus:ring-1 focus:ring-green-400 transition-all duration-300"
              />
              <p className="text-xs text-green-600 mt-1">
                Password is required for all operations. Use the same password for encrypt/decrypt.
              </p>
            </div>

            {/* Upload Area */}
            <div
              onDragEnter={handleDrag}
              onDragLeave={handleDrag}
              onDragOver={handleDrag}
              onDrop={handleDrop}
              className={`relative border-2 border-dashed rounded-xl p-12 mb-6 transition-all duration-300 ${
                dragActive
                  ? 'border-green-400 bg-green-500/10 shadow-[0_0_30px_rgba(0,255,65,0.3)]'
                  : 'border-green-500/40 hover:border-green-400/60 hover:bg-green-500/5'
              }`}
            >
              <input
                type="file"
                onChange={handleFileChange}
                className="absolute inset-0 w-full h-full opacity-0 cursor-pointer"
                accept={mode === 'decrypt' ? '.png' : '*'}
              />
              <div className="text-center">
                {file ? (
                  <>
                    <FileImage className="w-16 h-16 mx-auto mb-4 text-green-400 animate-bounce" />
                    <p className="text-lg text-green-400 mb-2 font-semibold truncate" title={file.name}>
                      {file.name.length > 30 ? `${file.name.substring(0, 27)}...` : file.name}
                    </p>
                    <p className="text-sm text-green-600">
                      {(file.size / 1024 / 1024).toFixed(2)} MB • {file.type || 'Unknown type'}
                    </p>
                  </>
                ) : (
                  <>
                    <Upload className="w-16 h-16 mx-auto mb-4 text-green-500/50 animate-pulse" />
                    <p className="text-lg text-green-500 mb-2">
                      {mode === 'crypt' ? 'Drop file to encrypt' : 'Drop PNG to decrypt'}
                    </p>
                    <p className="text-sm text-green-700">or click to browse</p>
                    <p className="text-xs text-green-800 mt-2">
                      Max: {mode === 'crypt' ? '10MB files' : '25MB images'}
                    </p>
                  </>
                )}
              </div>
            </div>

            {/* Process Button */}
            {file && (
              <div className="relative">
                <button
                  onClick={handleProcess}
                  disabled={loading}
                  className={`w-full py-4 px-6 font-bold rounded-lg transition-all duration-300 tracking-wider relative overflow-hidden ${
                    loading
                      ? 'bg-green-950/20 text-green-400 border border-green-500/40'
                      : 'bg-gradient-to-r from-green-600 to-green-500 hover:from-green-500 hover:to-green-400 text-black shadow-[0_0_30px_rgba(0,255,65,0.4)] hover:shadow-[0_0_50px_rgba(0,255,65,0.6)]'
                  }`}
                >
                  {loading ? (
                    <div className="flex items-center justify-center gap-3">
                      <span className="text-sm font-mono">{progress}%</span>
                      <span>PROCESSING...</span>
                    </div>
                  ) : (
                    <span className="flex items-center justify-center gap-3">
                      {mode === 'crypt' ? <Lock className="w-5 h-5" /> : <Unlock className="w-5 h-5" />}
                      {mode === 'crypt' ? 'ENCRYPT TO PNG' : 'DECRYPT FROM PNG'}
                    </span>
                  )}

                  {/* Progress Fill */}
                  {loading && (
                    <div
                      className="absolute inset-0 bg-gradient-to-r from-green-600 to-green-500 transition-all duration-500 ease-out rounded-lg"
                      style={{ width: `${progress}%` }}
                    />
                  )}
                </button>
              </div>
            )}

            {/* Result */}
            {result && (
              <div className={`mt-6 backdrop-blur-xl border rounded-lg p-6 shadow-[0_0_20px_rgba(0,255,65,0.15)] transition-all duration-500 animate-fade-in ${
                result.success
                  ? 'bg-green-950/20 border-green-500/40'
                  : 'bg-red-950/20 border-red-500/40'
              }`}>
                <div className="flex items-start gap-4">
                  {result.success ? (
                    <CheckCircle className="w-6 h-6 text-green-400 flex-shrink-0 mt-1" />
                  ) : (
                    <XCircle className="w-6 h-6 text-red-400 flex-shrink-0 mt-1" />
                  )}
                  <div className="flex-1">
                    <h3 className="text-lg font-semibold text-green-400 mb-3">
                      &gt; {result.success ? 'SUCCESS' : 'ERROR'}
                    </h3>
                     {result.success ? (
                       <div className="space-y-2 text-sm">
                         <div className="flex justify-between">
                           <span className="text-green-600">Output:</span>
                           <span className="text-white">{result.filename}</span>
                         </div>
                         <div className="flex justify-between">
                           <span className="text-green-600">Size:</span>
                           <span className="text-white">{result.size}</span>
                         </div>
                       </div>
                    ) : (
                      <p className="text-red-400">{result.message}</p>
                    )}
                  </div>
                </div>
              </div>
            )}
          </div>

          {/* Info Section */}
          <div className="mt-8 backdrop-blur-xl bg-black/20 border border-green-500/20 rounded-lg p-6">
            <div className="flex items-start gap-3">
              <Info className="w-5 h-5 text-green-600 flex-shrink-0 mt-0.5" />
              <div className="text-sm text-green-700 space-y-2">
                <p>&gt; Hide any file inside a PNG image</p>
                <p>&gt; Embedded metadata: signature, size, filename, SHA256 hash</p>
                <p>&gt; Cross-platform compatibility via ImageSharp</p>
                <p className="text-xs text-green-800 mt-4">
                  For privacy and experimentation. Use responsibly.
                </p>
              </div>
            </div>
          </div>
        </div>

        {/* Footer */}
        <footer className="text-center mt-12 text-green-800 text-sm">
          <p>&gt; ShadeOfColor2 - MIT License - github.com/archistico</p>
        </footer>
      </div>
    </div>
  );
}

export default App;