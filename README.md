# GPU N-Body Gravitational Astro-Simulation with Stereoscopic 3D

[![Unity 6](https://img.shields.io/badge/Unity-6000.6%20LTS-black?style=flat&logo=unity)](https://unity.com/)
[![DirectX 12](https://img.shields.io/badge/Graphics-DirectX%2012%20%2F%20Vulkan-0078D7?style=flat)](https://docs.microsoft.com/en-us/windows/win32/direct3d12/)
[![HLSL Compute](https://img.shields.io/badge/Compute-HLSL%20DirectCompute-critical?style=flat)](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/direct3d-11-advanced-stages-compute-shader)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A high-performance, computationally optimized **$N$-Body Gravitational Simulation** built in Unity 6, simulating **$65{,}536$ to $262{,}144+$ celestial bodies in real time** at 60–165+ FPS on modern GPUs.

Features direct GPU shared memory caching (LDS tiling), symplectic Leapfrog numerical integration, zero-copy procedural instanced billboard rendering, velocity-mapped spectral coloring, and a native **Cross-Eyed Stereoscopic 3D** viewport.

---

## Key Features

* **Massive Parallelism ($O(N^2)$ Compute with $O(N^2/B)$ Bandwidth)**: Utilizes HLSL compute shaders with 256-thread shared memory tiles (`groupshared float4`), reducing global VRAM bandwidth pressure by a factor of 256.
* **Symplectic Leapfrog Integration**: Preserves Hamiltonian phase-space volume and total angular momentum, preventing orbital decay or artificial expansion.
* **Zero-Copy Procedural Instancing (`Graphics.DrawMeshInstancedIndirect`)**: Simulation state stays strictly in GPU VRAM (`StructuredBuffer<Body>`). All particles are submitted in a single draw call without CPU readback.
* **Cross-Eyed & Parallel Stereoscopic 3D**: Dual asymmetric perspective viewports with dynamic toe-in convergence. Allows viewing the cosmos as a floating 3D hologram with live IPD (interpupillary distance) tuning and an auto-fused 3D HUD.
* **Astrophysical Presets**:
  * **Colliding Galaxies**: Two inclined spiral disk galaxies with central supermassive black holes undergoing tidal bridge and tail formation.
  * **Plummer Sphere**: Virialized globular star cluster in dynamical self-gravitating equilibrium ($2K + U = 0$).
  * **Accretion Disk**: Central stellar mass surrounded by a swirling Keplerian protoplanetary disk.
* **Velocity-Mapped Thermal Emission**: Stars are dynamically colored via a blackbody gradient (warm stellar amber $\to$ azure cyan $\to$ superheated relativistic white) with additive blending.

---

## Computational Architecture & Mathematical Foundation

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ 1. CPU INITIALIZATION (Assets/Scripts/NBodyController.cs)                   │
│    • Samples initial coordinates & Keplerian velocities                     │
│    • Allocates 32-byte structured buffers (pos + mass + vel + speed)        │
│    • Uploads to GPU VRAM once (data remains 100% on GPU)                   │
└──────────────────────────────────────┬──────────────────────────────────────┘
                                       │
                                       ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ 2. GPU COMPUTE KERNEL (Assets/Shaders/NBodySimulation.compute)              │
│    • Dispatch: 256 threads per group (matching hardware warp multiples)     │
│    • Tiled Caching: groupshared float4 s_sharedPosMass[256]                 │
│      - 256x reduction in global memory bandwidth pressure                   │
│      - Fast hardware rsqrt for gravitational force evaluation                │
│    • Symplectic Leapfrog Integration: preserves orbital phase space energy  │
└──────────────────────────────────────┬──────────────────────────────────────┘
                                       │
                                       ▼ (Double-Buffered VRAM Arrays)
┌─────────────────────────────────────────────────────────────────────────────┐
│ 3. ZERO-COPY INDIRECT RENDER (Assets/Shaders/NBodyParticle.shader)          │
│    • Graphics.DrawMeshInstancedIndirect: 1 single draw call for 65k+ stars  │
│    • Vertex Shader: Reads StructuredBuffer<Body> directly from VRAM         │
│    • Billboarding: Generates view-aligned quads in camera eye space         │
│    • Velocity Heatmap: Remaps speed from Amber -> Azure -> Hot White        │
│    • Additive Blending (Blend One One): Overlapping stars blend organically │
└──────────────────────────────────────┬──────────────────────────────────────┘
                                       │
                                       ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ 4. STEREOSCOPY & HUD (NBodyStereo.cs & NBodyUI.cs)                          │
│    • Cross-eyed dual viewports with toe-in convergence                     │
│    • Symmetrical floating 3D HUD fusing live metrics in stereo view         │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 1. The Physics Kernel

Every particle $i$ experiences the gravitational attraction of all particles $j$:

$$\vec{a}_i = \sum_{j=0, j \neq i}^{N-1} G \frac{m_j (\vec{r}_j - \vec{r}_i)}{\left(\|\vec{r}_j - \vec{r}_i\|^2 + \epsilon^2\right)^{3/2}}$$

* **Softening Length ($\epsilon$)**: Avoids numerical singularities during close encounters where $\|\vec{r}_j - \vec{r}_i\| \to 0$.
* **Symplectic Leapfrog Time Stepping**:
  $$\vec{v}_{t+\Delta t} = \vec{v}_t + \vec{a}_t \Delta t$$
  $$\vec{x}_{t+\Delta t} = \vec{x}_t + \vec{v}_{t+\Delta t} \Delta t$$
  Unlike Explicit Forward Euler (which artificially adds energy and causes orbits to expand), the symplectic leapfrog scheme preserves Hamiltonian invariants.

### 2. Threadgroup Shared Memory Caching (LDS)

In naive GPU $N$-body simulations, evaluating $N^2$ interactions requires billions of global memory transactions per frame. With $N = 65{,}536$:

$$\text{Interactions per frame} = 65{,}536^2 \approx 4.29 \times 10^9$$

Our kernel uses threadgroup shared memory tiling:
1. Each workgroup consists of $B = 256$ threads.
2. In each tile iteration, all 256 threads cooperatively fetch one body into `groupshared float4 s_sharedPosMass[256]` (on-chip L1/LDS cache).
3. `GroupMemoryBarrierWithGroupSync()` synchronizes the workgroup.
4. All threads calculate forces against this cached slice from ultra-fast register memory before loading the next tile.
5. This reduces global memory fetches from $O(N^2)$ to $O(N^2 / 256)$ — a **256x bandwidth reduction**.

---

## Stereoscopic 3D (Cross-Eyed & Parallel)

The simulation features a native stereoscopic rendering mode.

```
┌───────────────────────────────┬───────────────────────────────┐
│       LEFT SCREEN HALF        │       RIGHT SCREEN HALF       │
│                               │                               │
│     Right Eye Perspective     │     Left Eye Perspective      │
│          (+IPD / 2)           │          (-IPD / 2)           │
│                               │                               │
└───────────────────────────────┴───────────────────────────────┘
```

### How to View the 3D Hologram:
1. Press **`X`** to enter Stereoscopic Mode (the main menu automatically hides).
2. Slightly **cross your eyes** until the left and right halves overlap in the center.
3. Relax and focus on the central fused image: your visual cortex will perceive a **deep 3D volume** with foreground stars popping out and the galactic core receding into depth.
4. Tap **`Up Arrow` / `Down Arrow`** to adjust the 3D depth pop ($\text{IPD} \in [0.2, 40.0]$).
5. Tap **`Left Arrow` / `Right Arrow`** to adjust the focal convergence plane.
6. The floating stereo HUD is rendered symmetrically on both halves, fusing into a 3D status display.

---

## Controls & Hotkeys Reference

| Key / Control | Function | Description |
| :--- | :--- | :--- |
| **`X`** | **Toggle 3D Stereoscopy** | Switches between mono and stereoscopic split-screen. Hides main menu. |
| **`Up Arrow`** | **Increase 3D Depth** | Increases eye separation ($\text{IPD}$) by $+0.8$ per tap (or hold with Shift). |
| **`Down Arrow`** | **Decrease 3D Depth** | Decreases eye separation ($\text{IPD}$) by $-0.8$ per tap. |
| **`Right Arrow`** | **Push Focal Distance** | Shifts the zero-parallax convergence plane further back ($+10$ units). |
| **`Left Arrow`** | **Pull Focal Distance** | Brings the zero-parallax convergence plane closer ($-10$ units). |
| **`Z`** | **Swap Eyes** | Toggles between **Cross-Eyed** and **Parallel View** modes. |
| **`1`** | **Colliding Galaxies** | Spawns two inclined spiral disk galaxies with central black holes. |
| **`2`** | **Plummer Cluster** | Spawns a virialized spherical star cluster. |
| **`3`** | **Accretion Disk** | Spawns a central massive body with a Keplerian dust disk. |
| **`[` / `]`** | **Scale Body Count** | Cycles body count between **$16\text{K}$**, **$65\text{K}$**, **$131\text{K}$**, and **$262\text{K}$**. |
| **`P`** | **Pause / Resume** | Freezes/unfreezes gravitational integration. |
| **`C`** | **Camera Mode** | Toggles between **Free-Flight** (WASD) and **Orbit** mode. |
| **Hold RMB + WASD** | **6-DOF Navigation** | Smooth flight. Hold `Left Shift` for turbo boost; `E`/`Q` for vertical thrust. |
| **`R`** | **Reset Camera** | Snaps camera back to default cosmological vantage point. |
| **`H`** | **Toggle UI** | Hides or shows the diagnostics menu panel. |

---

## Performance Benchmarks

*Tested on NVIDIA GeForce RTX 5060 Laptop GPU (Direct3D 12):*

| Body Count ($N$) | Interactions / Frame | Gravitational Compute | Average FPS | Frame Time |
| :--- | :--- | :--- | :--- | :--- |
| **16,384** | 268.4 Million | ~885 GFLOPs | **165+ FPS** | ~6.0 ms |
| **65,536** | 4.29 Billion | ~5.1 TFLOPs | **60–85 FPS** | ~12.5 ms |
| **131,072** | 17.18 Billion | ~10.3 TFLOPs | **30–45 FPS** | ~24.0 ms |

---

## Project Structure

```
UnityNBody/
├── Assets/
│   ├── Editor/
│   │   └── NBodySceneBuilder.cs       # Automated scene builder & standalone player compiler
│   ├── Scenes/
│   │   └── NBodySimulationScene.unity # Configured deep-space simulation scene
│   ├── Scripts/
│   │   ├── NBodyController.cs         # Compute buffer lifecycle, presets, dispatch & draw loop
│   │   ├── NBodyCamera.cs             # 6-DOF free flight & orbital camera controller
│   │   ├── NBodyStereo.cs             # Cross-eyed / parallel stereoscopy & fused 3D HUD
│   │   └── NBodyUI.cs                 # Real-time diagnostics HUD & parameter sliders
│   └── Shaders/
│       ├── NBodySimulation.compute    # Tiled LDS HLSL gravity kernel & leapfrog integrator
│       ├── NBodyParticle.shader       # Procedural instanced billboard shader with glow
│       └── NBodyMaterial.mat          # Instanced particle material
├── Packages/
│   └── manifest.json                  # Unity package dependencies
├── ProjectSettings/                   # Engine & input configuration
├── .gitattributes                     # Git line-ending normalization & binary filters
├── .gitignore                         # GitHub standard Unity exclusion rules
├── LICENSE                            # MIT License
└── README.md                          # Project documentation
```

---

## Getting Started

### Option 1: Run the Precompiled Standalone Executable
Launch `Build/NBodySimulation.exe` directly on Windows with any modern GPU (DirectX 11, DirectX 12, or Vulkan).

### Option 2: Open in Unity 6
1. Open **Unity Hub** (version 3.8+).
2. Click **Add** $\to$ Select the `UnityNBody` project directory.
3. Open with **Unity 6 (`6000.6.7f1` or later)**.
4. Load `Assets/Scenes/NBodySimulationScene.unity`.
5. Press **Play** ($\blacktriangleright$) in the editor.

---

## License

This project is licensed under the [MIT License](LICENSE).
