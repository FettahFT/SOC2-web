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
      speeds[x] = Math.random() * 0.5 + 0.5; // Random speed between 0.5 and 1
      brightness[x] = Math.random() * 0.5 + 0.5; // Random brightness
    }
    
    const draw = () => {
      // Create fade effect with slight green tint
      ctx.fillStyle = 'rgba(0, 0, 0, 0.05)';
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      
      for (let i = 0; i < drops.length; i++) {
        // Vary character selection for more randomness
        const text = matrixArray[Math.floor(Math.random() * matrixArray.length)];
        
        // Calculate position
        const x = i * fontSize;
        const y = drops[i] * fontSize;
        
        // Create gradient effect for leading character (brighter)
        if (y < canvas.height - fontSize * 3) {
          // Trail characters (dimmer)
          const alpha = brightness[i] * 0.3;
          ctx.fillStyle = `rgba(0, 255, 65, ${alpha})`;
          ctx.font = fontSize + 'px monospace';
          ctx.fillText(text, x, y);
          
          // Add occasional bright flashes
          if (Math.random() > 0.98) {
            ctx.fillStyle = `rgba(0, 255, 65, ${brightness[i]})`;
            ctx.fillText(text, x, y);
          }
        }
        
        // Leading character (brightest with glow)
        ctx.fillStyle = `rgba(255, 255, 255, ${brightness[i] * 0.9})`;
        ctx.font = `bold ${fontSize}px monospace`;
        ctx.fillText(matrixArray[Math.floor(Math.random() * matrixArray.length)], x, y);
        
        // Add glow effect to leading character
        ctx.shadowBlur = 10;
        ctx.shadowColor = 'rgba(0, 255, 65, 0.8)';
        ctx.fillStyle = `rgba(0, 255, 65, ${brightness[i]})`;
        ctx.fillText(matrixArray[Math.floor(Math.random() * matrixArray.length)], x, y);
        ctx.shadowBlur = 0;
        
        // Move drop down with variable speed
        drops[i] += speeds[i];
        
        // Reset drop with variable probability
        if (drops[i] * fontSize > canvas.height && Math.random() > 0.975) {
          drops[i] = 0;
          speeds[i] = Math.random() * 0.5 + 0.5;
          brightness[i] = Math.random() * 0.5 + 0.5;
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
      style={{ zIndex: 1, opacity: 0.25 }}
    />
  );
};

export default MatrixRain;