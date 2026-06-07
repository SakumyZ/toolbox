description: 用于生成符合 ToolBox 项目风格的 3D 玻璃拟态 SVG 图标，保证风格统一。

# 3D 玻璃拟态 SVG 图标生成器 (Glassmorphism SVG Generator)

本 Skill 旨在指导 AI Agent 为 ToolBox 项目生成具有统一视觉风格的 SVG 图标。

## 🎨 风格设计规范

图标采用**现代 3D 玻璃微拟态（Glassmorphism）**风格，所有图标共享相同的技术规范和渐变参数：

1. **画布规格**：统一使用 `viewBox="0 0 100 100"`，`width="100"`，`height="100"`。
2. **渐变组定义（`<defs>`）**：
   - **`glassBodyGrad`（玻璃本体水平渐变）**：
     * 方向：水平（x1="0", y1="0", x2="1", y2="0"）
     * 渐变点：
       * 0%: 纯白 (`#FFFFFF`)，不透明度 `0.25`
       * 15%: 纯白 (`#FFFFFF`)，不透明度 `0.05`
       * 50%: 纯白 (`#FFFFFF`)，不透明度 `0.00`
       * 85%: 纯白 (`#FFFFFF`)，不透明度 `0.05`
       * 100%: 纯白 (`#FFFFFF`)，不透明度 `0.25`
   - **`glassOutlineGrad`（边缘轮廓对角渐变）**：
     * 方向：对角（x1="0", y1="0", x2="1", y2="1"）
     * 渐变点：
       * 0%: 纯白 (`#FFFFFF`)，不透明度 `0.90`
       * 50%: 浅灰 (`#E0E0E0`)，不透明度 `0.60`
       * 100%: 蓝灰 (`#B0BEC5`)，不透明度 `0.80`
   - **`colorGrad`（彩色本体渐变，每个图标自定义）**：
     * 方向：垂直（x1="0", y1="0", x2="0", y2="1"）
     * 使用色彩明亮、高饱和度的双色渐变，不透明度设置为 `0.85` ~ `0.95`。

3. **图层层级（双层夹心结构）**：
   - **底层/内嵌层**：代表图标主体含义的彩色实体路径（使用自定义 `colorGrad` 填充）。
   - **顶层/玻璃层**：玻璃质感外壳路径（填充使用 `url(#glassBodyGrad)`，描边使用 `url(#glassOutlineGrad)`，且 `stroke-width="2.5"`）。

4. **高光反射层（Highlights & Reflections）**：
   - **左上受光面**：一条纯白路径，`stroke="#FFFFFF"`，`stroke-width="2"`，`stroke-linecap="round"`，不透明度 `0.6` ~ `0.8`；在左上方放置一个小镜面圆点，`r="1.5"`，不透明度 `0.8`。
   - **右侧反射弧**：一条细白色路径，`stroke-width="1"`，不透明度 `0.35`，增添质感。

---

## 📝 统一生成提示词 (Master Prompt)

当需要生成新图标时，直接向绘图模型或代码生成模型输入以下提示：

```text
Generate a valid SVG code (viewBox="0 0 100 100", width="100", height="100") for [图标主体主题，例如：时钟/文件夹/齿轮].
The icon must use a horizontal glassBodyGrad for semi-transparent refraction, a diagonal glassOutlineGrad for the stroke-width="2.5" glass outline, and a vertical linear gradient for the inner vibrant colored element.
Include specular curve highlights (opacity 0.6-0.8) and a reflection dot on the upper left, and a thin reflection stroke on the right side.
Output ONLY the raw SVG code, keep paths clean and vector-perfect.
```

---

## 📂 经典实装样例对照 (Code Examples)

### 样例一：`water.svg` (喝水提醒)
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100">
  <defs>
    <linearGradient id="waterGrad" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0%" stop-color="#4FC3F7" stop-opacity="0.85"/>
      <stop offset="100%" stop-color="#0288D1" stop-opacity="0.95"/>
    </linearGradient>
    <linearGradient id="waterSurfaceGrad" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0%" stop-color="#E1F5FE" stop-opacity="0.95"/>
      <stop offset="100%" stop-color="#81D4FA" stop-opacity="0.9"/>
    </linearGradient>
    <linearGradient id="glassBodyGrad" x1="0" y1="0" x2="1" y2="0">
      <stop offset="0%" stop-color="#FFFFFF" stop-opacity="0.25"/>
      <stop offset="15%" stop-color="#FFFFFF" stop-opacity="0.05"/>
      <stop offset="50%" stop-color="#FFFFFF" stop-opacity="0.0"/>
      <stop offset="85%" stop-color="#FFFFFF" stop-opacity="0.05"/>
      <stop offset="100%" stop-color="#FFFFFF" stop-opacity="0.25"/>
    </linearGradient>
    <linearGradient id="glassOutlineGrad" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0%" stop-color="#FFFFFF" stop-opacity="0.9"/>
      <stop offset="50%" stop-color="#E0E0E0" stop-opacity="0.6"/>
      <stop offset="100%" stop-color="#B0BEC5" stop-opacity="0.8"/>
    </linearGradient>
  </defs>
  <path d="M 69,32 C 84,32 86,43 86,50 C 86,57 82,68 67,68" fill="none" stroke="url(#glassOutlineGrad)" stroke-width="4.5" opacity="0.8" stroke-linecap="round"/>
  <path d="M 69,32 C 84,32 86,43 86,50 C 86,57 82,68 67,68" fill="none" stroke="#FFFFFF" stroke-width="1.5" opacity="0.9" stroke-linecap="round"/>
  <path d="M 29.5,45 C 29.5,60 33,78 50,78 C 67,78 70.5,60 70.5,45 Z" fill="url(#waterGrad)"/>
  <ellipse cx="50" cy="45" rx="20.5" ry="4.5" fill="url(#waterSurfaceGrad)"/>
  <circle cx="42" cy="65" r="1.5" fill="#FFFFFF" opacity="0.6"/>
  <circle cx="58" cy="58" r="2" fill="#FFFFFF" opacity="0.5"/>
  <circle cx="48" cy="52" r="1" fill="#FFFFFF" opacity="0.7"/>
  <path d="M 28,25 C 28,60 32,80 50,80 C 68,80 72,60 72,25 Z" fill="url(#glassBodyGrad)" stroke="url(#glassOutlineGrad)" stroke-width="2.5" />
  <ellipse cx="50" cy="25" rx="22" ry="5" fill="none" stroke="#FFFFFF" stroke-width="2.2" opacity="0.85"/>
  <ellipse cx="50" cy="25" rx="22" ry="5" fill="#FFFFFF" opacity="0.05"/>
  <path d="M 32,30 C 32,48 35,66 43,72" fill="none" stroke="#FFFFFF" stroke-width="2.2" stroke-linecap="round" opacity="0.65"/>
  <circle cx="35" cy="38" r="1.8" fill="#FFFFFF" opacity="0.8"/>
  <path d="M 68,30 C 68,40 67,50 64,58" fill="none" stroke="#FFFFFF" stroke-width="1" stroke-linecap="round" opacity="0.35"/>
</svg>
```

### 样例二：`meeting.svg` (会议提醒)
```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100">
  <defs>
    <linearGradient id="orangeGrad" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0%" stop-color="#FFB74D" stop-opacity="0.85"/>
      <stop offset="100%" stop-color="#F57C00" stop-opacity="0.95"/>
    </linearGradient>
    <linearGradient id="glassBodyGrad" x1="0" y1="0" x2="1" y2="0">
      <stop offset="0%" stop-color="#FFFFFF" stop-opacity="0.25"/>
      <stop offset="15%" stop-color="#FFFFFF" stop-opacity="0.05"/>
      <stop offset="50%" stop-color="#FFFFFF" stop-opacity="0.0"/>
      <stop offset="85%" stop-color="#FFFFFF" stop-opacity="0.05"/>
      <stop offset="100%" stop-color="#FFFFFF" stop-opacity="0.25"/>
    </linearGradient>
    <linearGradient id="glassOutlineGrad" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0%" stop-color="#FFFFFF" stop-opacity="0.9"/>
      <stop offset="50%" stop-color="#E0E0E0" stop-opacity="0.6"/>
      <stop offset="100%" stop-color="#B0BEC5" stop-opacity="0.8"/>
    </linearGradient>
  </defs>
  <path d="M 32,24 L 54,24 C 59.5,24 64,28.5 64,34 L 64,46 C 64,51.5 59.5,56 54,56 L 29,56 L 22,62 L 26,55 C 23.5,53.5 22,50 22,46 L 22,34 C 22,28.5 26.5,24 32,24 Z" fill="url(#orangeGrad)"/>
  <line x1="30" y1="34" x2="52" y2="34" stroke="#FFFFFF" stroke-width="2" stroke-linecap="round" opacity="0.6"/>
  <line x1="30" y1="42" x2="44" y2="42" stroke="#FFFFFF" stroke-width="2" stroke-linecap="round" opacity="0.6"/>
  <path d="M 46,38 L 68,38 C 73.5,38 78,42.5 78,48 L 78,60 C 78,65.5 73.5,70 68,70 L 75,76 L 71,69 L 46,70 C 40.5,70 36,65.5 36,60 L 36,48 C 36,42.5 40.5,38 46,38 Z" fill="url(#glassBodyGrad)" stroke="url(#glassOutlineGrad)" stroke-width="2.5" />
  <circle cx="48" cy="54" r="2.5" fill="#FF9800"/>
  <circle cx="57" cy="54" r="2.5" fill="#FFB74D"/>
  <circle cx="66" cy="54" r="2.5" fill="#FFE0B2"/>
  <path d="M 46,39.5 C 42,39.5 39,41 39,44" fill="none" stroke="#FFFFFF" stroke-width="1.8" stroke-linecap="round" opacity="0.75"/>
  <path d="M 38,48 C 38,54 39,58 43,62" fill="none" stroke="#FFFFFF" stroke-width="1.8" stroke-linecap="round" opacity="0.5"/>
  <circle cx="41" cy="42" r="1" fill="#FFFFFF" opacity="0.8"/>
</svg>
```
