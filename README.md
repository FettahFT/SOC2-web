#  ShadeOfColor2 - Web UI

A purely client-side web application for hiding files inside PNG images using steganography. This project is a web-based user interface for the concepts demonstrated in the original [ShadeOfColor2 C# application by @archistico](https://github.com/archistico/ShadeOfColor2).

**[Live Demo Hosted on Vercel]** *(<- Add your Vercel deployment link here!)*

---

![image](https://github.com/user-attachments/assets/e5205f8a-c393-431c-889a-033011145099)


## ✨ Features

- **100% Client-Side:** All processing is done in your browser. Your files are never uploaded to a server, ensuring maximum privacy.
- **Hide & Extract:** Seamlessly hide any file within a PNG image and extract it back out.
- **Large File Support:** Process large files, limited only by your browser's capabilities.
- **Drag & Drop:** Modern, easy-to-use interface with a drag-and-drop file input.
- **Thematic UI:** A cool, hacker-themed interface built with React and Tailwind CSS.

## ⚙️ How It Works

The application reads the raw bytes of your file and embeds them into the color channels (Red, Green, and Blue) of the pixels in a newly generated PNG image. A custom header is embedded at the beginning of the image data, containing metadata like the original filename, file size, and a SHA256 hash to verify data integrity upon extraction.

The alpha channel of the image is left untouched to prevent data corruption from premultiplied alpha, a common issue in canvas-based image manipulation.

## 🛠️ Tech Stack

- **Frontend:** [React](https://reactjs.org/)
- **Styling:** [Tailwind CSS](https://tailwindcss.com/)
- **Core Logic:** Standard Browser APIs (Canvas, Web Crypto)

## 🚀 How to Use

1.  **Visit the web app.**
2.  **Choose a mode:**
    - **HIDE:** To hide a file inside an image.
    - **EXTRACT:** To get a file from an image.
3.  **Drag and drop** your file (any type for HIDE, a PNG for EXTRACT).
4.  Click the **"HIDE IN PNG"** or **"EXTRACT FROM PNG"** button.
5.  Your browser will automatically download the resulting file.

## 💻 Running Locally

To run this project on your own machine:

1.  **Clone the repository:**
    ```bash
    git clone https://github.com/your-username/your-repo-name.git
    cd your-repo-name
    ```

2.  **Install dependencies:**
    ```bash
    npm install
    ```

3.  **Start the development server:**
    ```bash
    npm start
    ```
    This will open the app in your browser at `http://localhost:3000`.

4.  **Build for production:**
    ```bash
    npm run build
    ```
    This creates an optimized build in the `build` folder, which you can deploy to any static hosting service like Vercel or GitHub Pages.

## 🙏 Credits

This project is a fork and a web-based re-imagining of the original C# console application **[ShadeOfColor2](https://github.com/archistico/ShadeOfColor2)** by **[@archistico](https://github.com/archistico)**. The core steganographic concept and header design are inspired by their work.