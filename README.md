# RoomAlive Toolkit

The RoomAlive Toolkit enables creation of immersive, dynamic projection mapping experiences. Originally developed at Microsoft Research, it has powered projects such as [RoomAlive](https://www.youtube.com/watch?v=ILb5ExBzHqw), [IllumiRoom](https://www.youtube.com/watch?v=re1EatGRV0w), [ManoAMano](https://www.youtube.com/watch?v=Df7fZAYVAIE), [Beamatron](https://www.youtube.com/watch?v=Z4bdrG8S1FM), and [Room2Room](https://www.youtube.com/watch?v=tRzOqTRxoek).

The toolkit calibrates arrays of projectors and depth cameras, then uses the resulting calibration data to render view-dependent projected imagery onto arbitrary room geometry in real time.

![RoomAlive Scene](RoomAliveToolkitForUnity/docs/images/Roomalive.png?raw=true)

*An example RoomAlive scene using 6 projectors and 6 depth cameras.*

---

## What It Does

1. **Calibrates projectors and depth cameras** -- Uses structured light (Gray code patterns) to establish pixel-level correspondences between each projector and each camera. A RANSAC + Levenberg-Marquardt optimization pipeline recovers projector intrinsics (focal length, principal point, lens distortion) and extrinsics (pose relative to cameras).

2. **Captures room geometry** -- Averages depth frames to build a 3D mesh of the room, then textures it with the color camera feed.

3. **Renders view-dependent projection** -- Given a user's head position (via skeleton tracking), renders the virtual scene from the user's viewpoint into a texture, then re-projects that texture from each projector's calibrated viewpoint onto the room geometry. The result is imagery that appears correct from the user's perspective.

4. **Streams depth/color/skeleton data to Unity** -- A standalone server captures sensor data and streams it over TCP to Unity, where it drives real-time depth mesh rendering and interaction.

---

## Architecture

```
                          Calibration Pipeline
                          ====================

   Projector(s)                                      Depth Camera(s)
       |                                                   |
       | Gray code patterns                                | Depth + Color frames
       v                                                   v
  ProjectorServer ─────── gRPC (port 9001) ──────── KinectServer (port 9000)
       |                                                   |
       └──────────────── CalibrateEnsemble ────────────────┘
                                |
                                v
                        ensemble.xml (calibration data)
                                |
                ┌───────────────┼───────────────┐
                v               v               v
     ProjectionMapping     KinectV2Server    Unity Client
       Sample (D3D11)     (TCP streaming)   (RoomAliveUnity)
```

### Data Flow

- **Calibration phase**: `CalibrateEnsemble` orchestrates the process. It tells each `ProjectorServer` to display Gray code patterns while each `KinectServer` captures images. The captured correspondences feed into DLT + RANSAC + Levenberg-Marquardt optimization to solve for all camera/projector poses and intrinsics. Results are saved as `ensemble.xml`.

- **Runtime phase (Direct3D)**: `ProjectionMappingSample` loads `ensemble.xml`, connects to depth servers, renders depth meshes with color textures from the user's viewpoint, then re-projects onto each projector's frustum.

- **Runtime phase (Unity)**: `KinectV2Server` streams depth, color, skeleton, and audio over TCP to `RATKinectClient` in Unity. Unity reconstructs depth meshes via GPU shaders and applies view-dependent projection mapping through a multi-pass rendering pipeline.

---

## Repository Structure

```
RoomAliveToolkit/
├── ProCamCalibration/                    # .NET projects (C#)
│   ├── ProCamEnsembleCalibration/        # Core shared library
│   │   ├── CameraMath.cs                 # Projection, undistortion, DLT, plane fitting
│   │   ├── LevenbergMarquardt.cs         # Nonlinear least squares optimizer
│   │   ├── ProjectorCameraEnsemble.cs    # Calibration pipeline + data model
│   │   ├── Kinect2Calibration.cs         # Sensor calibration (extends SensorCalibration)
│   │   ├── IDepthSensor.cs              # Sensor abstraction interface
│   │   ├── AzureKinectSensor.cs         # Stub for Azure Kinect / Orbbec sensors
│   │   ├── KinectClient.cs             # gRPC client for depth server
│   │   ├── ProjectorClient.cs          # gRPC client for projector server
│   │   ├── GraphicsTransforms.cs        # Projection/view matrix construction
│   │   ├── Matrix.cs                    # Dense matrix class (wraps MathNet.Numerics)
│   │   ├── GrayCode.cs                  # Gray code pattern generation/decoding
│   │   └── *Image.cs                    # Unmanaged image types (Byte, Short, Float, ARGB, etc.)
│   │
│   ├── KinectServer/                     # Depth sensor streaming server (gRPC)
│   ├── ProjectorServer/                  # Projector display control server (gRPC + Direct2D)
│   ├── CalibrateEnsemble/               # WinForms GUI for calibration workflow
│   ├── ProjectionMappingSample/          # Direct3D 11 projection mapping demo
│   ├── Shaders/                          # HLSL shader source (SM 5.0)
│   │   ├── DepthAndColor*.hlsl          # Depth mesh rendering with color texture
│   │   ├── DepthAndProjectiveTexture*.hlsl  # View-dependent re-projection
│   │   ├── Mesh*.hlsl                   # OBJ mesh rendering with Phong lighting
│   │   ├── BilateralFilterPS.hlsl       # Depth smoothing filter
│   │   └── FullScreenQuad*.hlsl         # Post-processing infrastructure
│   └── Proto/                            # gRPC protocol buffer definitions
│       ├── kinect_server.proto
│       └── projector_server.proto
│
└── RoomAliveToolkitForUnity/             # Unity integration
    ├── RoomAliveKinectServer/            # Standalone sensor streaming server
    │   ├── KinectV2Server/              # WinForms app that captures + streams sensor data
    │   └── ConsoleTextBox/              # Console output UI component
    │
    └── RoomAliveUnity/                   # Unity project
        └── Assets/RoomAliveToolkit/
            ├── Scripts/
            │   ├── Kinect/              # Network client, depth mesh, skeleton, playback
            │   ├── Projection/          # Projector management, projection passes, view-dependent rendering
            │   ├── Users/               # User tracking, view camera
            │   ├── Editor/              # Unity Editor extensions
            │   └── Utilities/           # Standalone math/image classes (no external deps)
            └── Shaders/
                ├── DepthMeshSurfaceShader.shader        # GPU depth mesh reconstruction + RGB texturing
                ├── ProjectionMapping*.shader             # View-dependent projection mapping
                ├── ReplacementColor*.shader              # User-view rendering passes
                ├── DynamicMaskShader.shader              # Region masking
                └── DepthMeshProcessing.cginc             # Shared depth unprojection + calibration math
```

---

## Technology Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| Core library | .NET | 10.0 |
| Windows apps | .NET + WinForms | 10.0-windows |
| 3D rendering | Vortice.Windows (DirectX 11) | 3.8 |
| 2D rendering | Vortice.Direct2D1 | 3.8 |
| Linear algebra | MathNet.Numerics | 5.0 |
| Inter-service communication | gRPC | 2.67 |
| Image I/O | System.Drawing.Common | 9.0 |
| Math types | System.Numerics (Matrix4x4, Vector3) | built-in |
| GPU shaders | HLSL Shader Model 5.0 | DirectX 11 |
| Unity integration | Unity 2022 LTS+ | .NET Standard 2.1 |
| Depth sensor | IDepthSensor abstraction | pluggable |

---

## Prerequisites

### For ProCamCalibration (calibration + Direct3D sample)

- Windows 10/11 (DirectX 11 required)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or .NET 8+)
- Visual Studio 2022 (recommended) or `dotnet` CLI
- A supported depth camera (see [Sensor Support](#sensor-support))
- One or more projectors

### For Unity Integration

- Unity 2022 LTS or newer (Built-in Render Pipeline)
- Windows for the KinectV2Server streaming application
- Network connectivity between the server and Unity client

---

## Getting Started

### 1. Build the Solution

```bash
# Clone the repository
git clone https://github.com/CPCJ79/RoomAliveToolkit.git
cd RoomAliveToolkit

# Build the core library (cross-platform)
dotnet build ProCamCalibration/ProCamEnsembleCalibration/ProCamEnsembleCalibrationLib.csproj

# Build all Windows projects
dotnet build ProCamCalibration/ProCamEnsembleCalibration.sln

# Build the Unity-side server
dotnet build RoomAliveToolkitForUnity/RoomAliveKinectServer/KinectV2Server/KinectV2Server.csproj
```

### 2. Compile Shaders

The HLSL shaders in `ProCamCalibration/Shaders/` must be compiled to `.cso` files. If using Visual Studio, open the solution and build the Shaders project. Alternatively, use `dxc.exe`:

```bash
dxc.exe -T vs_5_0 -Fo Output/DepthAndColorVS.cso Shaders/DepthAndColorVS.hlsl
# Repeat for each shader...
```

### 3. Calibrate

See the detailed [ProCamCalibration tutorial](ProCamCalibration/README.md) for step-by-step calibration instructions. The basic workflow is:

1. Start `KinectServer` on the machine connected to each depth camera
2. Start `ProjectorServer` on each machine connected to a projector
3. Run `CalibrateEnsemble` and create a new configuration (File > New)
4. Enter the hostnames/IPs and display indices for each camera and projector
5. Run Calibrate > Acquire to capture Gray code images
6. Run Calibrate > Solve to compute calibration
7. Save the result as `ensemble.xml`

### 4. Run the Projection Mapping Sample

```bash
dotnet run --project ProCamCalibration/ProjectionMappingSample -- path/to/ensemble.xml
```

### 5. Use with Unity

1. Copy `ensemble.xml` into your Unity project's Assets folder
2. Open the RoomAliveUnity project in Unity 2022+
3. Start `KinectV2Server` on the sensor machine
4. In Unity, use `RATSceneSetup` to automatically build the scene from calibration data
5. Configure `RATKinectClient` with the server's IP address
6. Press Play

---

## Sensor Support

The toolkit uses an `IDepthSensor` abstraction that decouples the calibration and streaming code from any specific sensor SDK. Currently:

| Sensor | Status | Notes |
|--------|--------|-------|
| Kinect v2 | Legacy (original) | SDK discontinued; hardware no longer manufactured |
| Azure Kinect DK | Stub implementation | `AzureKinectSensor.cs` — needs `Microsoft.Azure.Kinect.Sensor` NuGet |
| Orbbec Femto Bolt | Recommended | Azure Kinect SDK-compatible; actively manufactured |
| Intel RealSense | Not yet implemented | Could implement `IDepthSensor` with librealsense |

To add a new sensor, implement the `IDepthSensor` interface:

```csharp
public interface IDepthSensor : IDisposable
{
    SensorCalibration Calibration { get; }
    byte[] AcquireDepthFrame();       // uint16 depth in mm
    byte[] AcquireColorFrameYUV();    // YUY2 format
    byte[] AcquireColorFrameRGB();    // BGRA format
    byte[] AcquireColorFrameJPEG();   // JPEG compressed
    bool SupportsBodyTracking { get; }
    TrackedBody[] AcquireBodyFrame();
    float LastColorGain { get; }
    long LastColorExposureTimeTicks { get; }
}
```

---

## Key Algorithms

### Calibration Pipeline

1. **Gray Code Structured Light** -- Binary Gray code patterns are projected and captured to establish dense pixel correspondences between each projector and each camera.

2. **Direct Linear Transform (DLT)** -- Initial estimate of the projector's projection matrix from 3D-to-2D point correspondences. Handles both planar and non-planar scenes.

3. **RANSAC** -- Random sample consensus to robustly filter outliers from the correspondence set. Each iteration fits a camera model to a random subset, then counts inliers within a 2-pixel threshold.

4. **Levenberg-Marquardt Optimization** -- Nonlinear least squares refinement of camera intrinsics (focal length, principal point, radial distortion k1/k2) and extrinsics (rotation + translation). Uses numerical Jacobian computation.

5. **Global Pose Optimization** -- Joint optimization of all camera and projector poses to minimize total reprojection error across the entire ensemble.

### Depth Mesh Rendering (GPU)

The depth-to-mesh pipeline runs entirely on the GPU:

1. A lookup table maps each depth pixel to a camera-space ray direction
2. The vertex shader multiplies the ray by the depth value to get a 3D point
3. A geometry shader culls triangles that straddle depth discontinuities (> 0.1m edge length)
4. The pixel shader maps the RGB color texture onto the mesh using calibrated depth-to-color transforms with lens distortion correction

### View-Dependent Projection Mapping

The two-pass rendering pipeline:

1. **User View Pass** -- Render the virtual scene from the tracked user's head position into an off-screen texture
2. **Projector Pass** -- For each projector, render the depth mesh from the projector's calibrated viewpoint, sampling the user-view texture using the user's projection matrix. This "undoes" the projector's perspective so the imagery appears correct from the user's viewpoint.

---

## Network Architecture

### gRPC Services (Calibration)

| Service | Port | Purpose |
|---------|------|---------|
| KinectServer | 9000 | Streams depth, color, calibration data |
| ProjectorServer | 9001 | Controls projector displays, Gray code patterns |

### TCP Streaming (Unity Runtime)

| Port | Stream | Format |
|------|--------|--------|
| 10010 | Depth | 4-byte length prefix + timestamp + uint16 pixels |
| 10011 | Color | 4-byte length prefix + timestamp + JPEG/YUV data |
| 10004 | Audio | 4-byte length prefix + timestamp + PCM samples |
| 10005 | Skeleton | 4-byte length prefix + timestamp + joint data |
| 10009 | Configuration | Request/response for calibration data |

---

## Calibration Data Format

Calibration results are stored as XML (`ensemble.xml`) using `DataContractSerializer`:

```xml
<ProjectorCameraEnsemble>
  <cameras>
    <Camera>
      <name>0</name>
      <hostNameOrAddress>192.168.1.10</hostNameOrAddress>
      <pose><!-- 4x4 world transform --></pose>
      <calibration>
        <colorCameraMatrix><!-- 3x3 intrinsics --></colorCameraMatrix>
        <colorLensDistortion><!-- k1, k2 --></colorLensDistortion>
        <depthCameraMatrix><!-- 3x3 intrinsics --></depthCameraMatrix>
        <depthLensDistortion><!-- k1, k2 --></depthLensDistortion>
        <depthToColorTransform><!-- 4x4 rigid transform --></depthToColorTransform>
      </calibration>
    </Camera>
  </cameras>
  <projectors>
    <Projector>
      <name>0</name>
      <hostNameOrAddress>192.168.1.20</hostNameOrAddress>
      <displayIndex>1</displayIndex>
      <width>1920</width>
      <height>1080</height>
      <cameraMatrix><!-- 3x3 intrinsics --></cameraMatrix>
      <lensDistortion><!-- k1, k2 --></lensDistortion>
      <pose><!-- 4x4 world transform --></pose>
    </Projector>
  </projectors>
</ProjectorCameraEnsemble>
```

---

## Citations

If you use this toolkit in your research, please cite:

```bibtex
@inproceedings{Jones:2014:RME:2642918.2647383,
  author = {Jones, Brett and Sodhi, Rajinder and Murdock, Michael and Mehra, Ravish
            and Benko, Hrvoje and Wilson, Andrew and Ofek, Eyal and MacIntyre, Blair
            and Raghuvanshi, Nikunj and Shapira, Lior},
  title = {RoomAlive: Magical Experiences Enabled by Scalable, Adaptive Projector-camera Units},
  booktitle = {Proceedings of the 27th Annual ACM Symposium on User Interface Software and Technology},
  series = {UIST '14},
  year = {2014},
  pages = {637--644},
  doi = {10.1145/2642918.2647383},
  publisher = {ACM},
}
```

## Contributing

We welcome contributions! Please [file an issue](https://github.com/CPCJ79/RoomAliveToolkit/issues) before making large changes so we can discuss the approach.

## License

This project is licensed under the [MIT License](LICENSE).
