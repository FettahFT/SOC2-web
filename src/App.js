import React, { useEffect, useRef } from 'react';

const MatrixRain = () => {
  const canvasRef = useRef(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    const ctx = canvas.getContext('2d');
    
    canvas.width = window.innerWidth;
    canvas.height = window.innerHeight;
    
    const matrix = "ABCDEFGHIJKLMNOPQRSTUVWXYZ123456789@#$%^&*()*&^%+-/~{[|`]}";
    const matrixArray = matrix.split("");
    
    const fontSize = 14;
    const columns = canvas.width / fontSize;
    
    const drops = [];
    const speeds = [];
    const brightness = [];
    
    for (let x = 0; x < columns; x++) {
      drops[x] = Math.floor(Math.random() * canvas.height / fontSize);
      speeds[x] = Math.random() * 0.5 + 0.5;
      brightness[x] = Math.random() * 0.4 + 0.6;
    }
    
    const draw = () => {
      ctx.fillStyle = 'rgba(0, 0, 0, 0.05)';
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      
      for (let i = 0; i < drops.length; i++) {
        const text = matrixArray[Math.floor(Math.random() * matrixArray.length)];
        
        const x = i * fontSize;
        const y = drops[i] * fontSize;
        
        if (y < canvas.height - fontSize * 3) {
          const alpha = brightness[i] * 0.25;
          ctx.fillStyle = `rgba(34, 197, 94, ${alpha})`;
          ctx.font = fontSize + 'px monospace';
          ctx.fillText(text, x, y);
          
          if (Math.random() > 0.975) {
            ctx.fillStyle = `rgba(34, 197, 94, ${brightness[i] * 0.6})`;
            ctx.fillText(text, x, y);
          }
        }
        
        ctx.fillStyle = `rgba(200, 255, 200, ${brightness[i] * 0.9})`;
        ctx.font = `bold ${fontSize}px monospace`;
        ctx.fillText(matrixArray[Math.floor(Math.random() * matrixArray.length)], x, y);
        
        ctx.shadowBlur = 8;
        ctx.shadowColor = 'rgba(34, 197, 94, 0.6)';
        ctx.fillStyle = `rgba(34, 197, 94, ${brightness[i] * 0.8})`;
        ctx.fillText(matrixArray[Math.floor(Math.random() * matrixArray.length)], x, y);
        ctx.shadowBlur = 0;
        
        drops[i] += speeds[i];
        
        if (drops[i] * fontSize > canvas.height && Math.random() > 0.975) {
          drops[i] = 0;
          speeds[i] = Math.random() * 0.5 + 0.5;
          brightness[i] = Math.random() * 0.4 + 0.6;
        }
      }
    };
    
    const interval = setInterval(draw, 50);
    
    const handleResize = () => {
      canvas.width = window.innerWidth;
      canvas.height = window.innerHeight;
    };
    
    window.addEventListener('resize', handleResize);
    
    return () => {
      clearInterval(interval);
      window.removeEventListener('resize', handleResize);
    };
  }, []);

  return (
    <canvas
      ref={canvasRef}
      className="fixed top-0 left-0 w-full h-full pointer-events-none"
      style={{ zIndex: 1, opacity: 0.2 }}
    />
  );
};

export default MatrixRain;