using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RoomAliveToolkit
{
    public class ProjectionMappingSample : ApplicationContext
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.Run(new ProjectionMappingSample(args));
        }

        public ProjectionMappingSample(string[] args)
        {
            // load ensemble.xml
            string path = args[0];
            string directory = Path.GetDirectoryName(path);
            ensemble = RoomAliveToolkit.ProjectorCameraEnsemble.FromFile(path);

            // create d3d device
            var factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
            factory.EnumAdapters(0, out var adapter);

            // When using DeviceCreationFlags.Debug on Windows 10, ensure that "Graphics Tools" are installed via Settings/System/Apps & features/Manage optional features.
            // Also, when debugging in VS, "Enable native code debugging" must be selected on the project.
            D3D11.D3D11CreateDevice(adapter, Vortice.Direct3D.DriverType.Unknown, DeviceCreationFlags.None, null, out device);

            // shaders
            depthAndColorShader = new DepthAndColorShader(device);
            projectiveTexturingShader = new ProjectiveTexturingShader(device);
            passThroughShader = new PassThrough(device, userViewTextureWidth, userViewTextureHeight);
            radialWobbleShader = new RadialWobble(device, userViewTextureWidth, userViewTextureHeight);
            meshShader = new MeshShader(device);
            fromUIntPS = new FromUIntPS(device, Kinect2Calibration.depthImageWidth, Kinect2Calibration.depthImageHeight);
            bilateralFilter = new BilateralFilter(device, Kinect2Calibration.depthImageWidth, Kinect2Calibration.depthImageHeight);

            // create device objects for each camera
            foreach (var camera in ensemble.cameras)
                cameraDeviceResources[camera] = new CameraDeviceResource(device, camera, renderLock, directory);

            // one user view
            // user view render target, depth buffer, viewport for user view
            var userViewTextureDesc = new Texture2DDescription()
            {
                Width = (uint)userViewTextureWidth,
                Height = (uint)userViewTextureHeight,
                MipLevels = 1, // revisit this; we may benefit from mipmapping?
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.None,
            };
            var userViewRenderTarget = device.CreateTexture2D(userViewTextureDesc);
            userViewRenderTargetView = device.CreateRenderTargetView(userViewRenderTarget);
            userViewSRV = device.CreateShaderResourceView(userViewRenderTarget);

            var filteredUserViewRenderTarget = device.CreateTexture2D(userViewTextureDesc);
            filteredUserViewRenderTargetView = device.CreateRenderTargetView(filteredUserViewRenderTarget);
            filteredUserViewSRV = device.CreateShaderResourceView(filteredUserViewRenderTarget);

            // user view depth buffer
            var userViewDpethBufferDesc = new Texture2DDescription()
            {
                Width = (uint)userViewTextureWidth,
                Height = (uint)userViewTextureHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.D32_Float, // necessary?
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.DepthStencil,
                CPUAccessFlags = CpuAccessFlags.None
            };
            var userViewDepthStencil = device.CreateTexture2D(userViewDpethBufferDesc);
            userViewDepthStencilView = device.CreateDepthStencilView(userViewDepthStencil);

            // user view viewport
            userViewViewport = new Viewport(0, 0, userViewTextureWidth, userViewTextureHeight, 0f, 1f);

            // create a form for each projector
            foreach (var projector in ensemble.projectors)
            {
                var form = new ProjectorForm(factory, device, renderLock, projector);
                if (fullScreenEnabled)
                    form.FullScreen = fullScreenEnabled; // TODO: fix this so can be called after Show
                form.Show();
                projectorForms.Add(form);
            }

            // example 3d object
            var mesh = Mesh.FromOBJFile("Content/FloorPlan.obj");
            meshDeviceResources = new MeshDeviceResources(device, mesh);

            // desktop duplication
            adapter.EnumOutputs(0, out var output);
            var output1 = output.QueryInterface<IDXGIOutput1>();
            outputDuplication = output1.DuplicateOutput(device);


            userViewForm = new Form1(factory, device, renderLock);
            userViewForm.Text = "User View";
            userViewForm.Show();


            userViewForm.videoPanel1.MouseClick += videoPanel1_MouseClick;

            // connect to local camera to acquire head position
            if (localHeadTrackingEnabled)
            {
                // TODO: Replace with IDepthSensor-based head tracking
                Console.WriteLine("Local head tracking requires IDepthSensor implementation");
                //new System.Threading.Thread(LocalBodyLoop).Start();
            }


            if (liveDepthEnabled)
            {
                foreach (var cameraDeviceResource in cameraDeviceResources.Values)
                    cameraDeviceResource.StartLive();
            }


            new System.Threading.Thread(RenderLoop).Start();
        }

        void videoPanel1_MouseClick(object sender, MouseEventArgs e)
        {
            alpha = 0;
        }


        IDXGIOutputDuplication outputDuplication;


        const int userViewTextureWidth = 2000;
        const int userViewTextureHeight = 1000;
        List<ProjectorForm> projectorForms = new List<ProjectorForm>();
        DepthAndColorShader depthAndColorShader;
        ProjectiveTexturingShader projectiveTexturingShader;
        Dictionary<ProjectorCameraEnsemble.Camera, CameraDeviceResource> cameraDeviceResources = new Dictionary<ProjectorCameraEnsemble.Camera, CameraDeviceResource>();
        Object renderLock = new Object();
        ID3D11RenderTargetView userViewRenderTargetView, filteredUserViewRenderTargetView;
        ID3D11DepthStencilView userViewDepthStencilView;
        ID3D11ShaderResourceView userViewSRV, filteredUserViewSRV, desktopTextureSRV;
        Viewport userViewViewport;
        ID3D11Device device;
        ProjectorCameraEnsemble ensemble;
        Form1 userViewForm;
        MeshShader meshShader;
        MeshDeviceResources meshDeviceResources;
        PassThrough passThroughShader;
        RadialWobble radialWobbleShader;
        FromUIntPS fromUIntPS;
        BilateralFilter bilateralFilter;
        System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        PointLight pointLight = new PointLight();
        ID3D11Texture2D desktopTexture;

        float alpha = 1;

        // TODO: make so these can be changed live, put in menu
        bool threeDObjectEnabled = Properties.Settings.Default.ThreeDObjectEnabled;
        bool wobbleEffectEnabled = Properties.Settings.Default.WobbleEffectEnabled;
        bool localHeadTrackingEnabled = Properties.Settings.Default.LocalHeadTrackingEnabled;
        bool liveDepthEnabled = Properties.Settings.Default.LiveDepthEnabled;
        bool fullScreenEnabled = Properties.Settings.Default.FullScreenEnabled;
        bool desktopDuplicationEnabled = Properties.Settings.Default.DesktopDuplicationEnabled;

        void RenderLoop()
        {

            while (true)
            {
                lock (renderLock)
                {
                    var deviceContext = device.ImmediateContext;

                    // render user view
                    deviceContext.ClearRenderTargetView(userViewRenderTargetView, new Color4(0, 0, 0, 1));
                    deviceContext.ClearDepthStencilView(userViewDepthStencilView, DepthStencilClearFlags.Depth, 1, 0);

                    Vector3 headPosition = new Vector3(0f, 1.1f, -1.4f); // may need to change this default

                    if (localHeadTrackingEnabled)
                    {
                        float distanceSquared = 0;
                        lock (headPositionLock)
                        {
                            headPosition = trackedHeadPosition;

                            float dx = handLeftPosition.X - handRightPosition.X;
                            float dy = handLeftPosition.Y - handRightPosition.Y;
                            float dz = handLeftPosition.Z - handRightPosition.Z;
                            distanceSquared = dx * dx + dy * dy + dz * dz;
                        }
                        var transform = Matrix4x4.CreateRotationY((float)Math.PI) * Matrix4x4.CreateTranslation(0, 0.45f, 0);
                        headPosition = Vector3.Transform(headPosition, transform);

                        if (trackingValid && (distanceSquared < 0.02f) && (alpha > 1))
                            alpha = 0;
                    }

                    var userView = GraphicsTransforms.LookAt(headPosition, headPosition + Vector3.UnitZ, Vector3.UnitY);
                    userView = Matrix4x4.Transpose(userView);


                    float aspect = (float)userViewTextureWidth / (float)userViewTextureHeight;
                    var userProjection = GraphicsTransforms.PerspectiveFov(55.0f / 180.0f * (float)Math.PI, aspect, 0.001f, 1000.0f);
                    userProjection = Matrix4x4.Transpose(userProjection);

                    // smooth depth images
                    foreach (var camera in ensemble.cameras)
                    {
                        var cameraDeviceResource = cameraDeviceResources[camera];
                        if (cameraDeviceResource.depthImageChanged)
                        {
                            fromUIntPS.Render(deviceContext, cameraDeviceResource.depthImageTextureRV, cameraDeviceResource.floatDepthImageRenderTargetView);
                            for (int i = 0; i < 1; i++)
                            {
                                bilateralFilter.Render(deviceContext, cameraDeviceResource.floatDepthImageRV, cameraDeviceResource.floatDepthImageRenderTargetView2);
                                bilateralFilter.Render(deviceContext, cameraDeviceResource.floatDepthImageRV2, cameraDeviceResource.floatDepthImageRenderTargetView);
                            }
                            cameraDeviceResource.depthImageChanged = false;
                        }
                    }

                    // wobble effect
                    if (wobbleEffectEnabled)
                        foreach (var camera in ensemble.cameras)
                        {
                            var cameraDeviceResource = cameraDeviceResources[camera];

                            var world = new Matrix4x4();
                            for (int i = 0; i < 4; i++)
                                for (int j = 0; j < 4; j++)
                                    world[i, j] = (float)camera.pose[i, j];
                            world = Matrix4x4.Transpose(world);

                            // view and projection matrix are post-multiply
                            var userWorldViewProjection = world * userView * userProjection;

                            depthAndColorShader.SetConstants(deviceContext, camera.calibration, userWorldViewProjection);
                            depthAndColorShader.Render(deviceContext, cameraDeviceResource.floatDepthImageRV, cameraDeviceResource.colorImageTextureRV, cameraDeviceResource.vertexBuffer, userViewRenderTargetView, userViewDepthStencilView, userViewViewport);
                        }

                    // 3d object
                    if (threeDObjectEnabled)
                    {
                        var world = Matrix4x4.CreateScale(1.0f) * Matrix4x4.CreateRotationY(90.0f / 180.0f * (float)Math.PI) *
                            Matrix4x4.CreateRotationX(-40.0f / 180.0f * (float)Math.PI) * Matrix4x4.CreateTranslation(0, 0.7f, 0.0f);

                        var pointLight = new PointLight();
                        pointLight.position = new Vector3(0, 2, 0);
                        pointLight.Ia = new Vector3(0.1f, 0.1f, 0.1f);
                        meshShader.SetVertexShaderConstants(deviceContext, world, userView * userProjection, pointLight.position);
                        meshShader.Render(deviceContext, meshDeviceResources, pointLight, userViewRenderTargetView, userViewDepthStencilView, userViewViewport);
                    }

                    // wobble effect
                    if (wobbleEffectEnabled)
                    {
                        alpha += 0.05f;
                        if (alpha > 1)
                            radialWobbleShader.SetConstants(deviceContext, 0);
                        else
                            radialWobbleShader.SetConstants(deviceContext, alpha);
                        radialWobbleShader.Render(deviceContext, userViewSRV, filteredUserViewRenderTargetView);
                    }

                    // desktop duplication
                    if (desktopDuplicationEnabled)
                    {
                        // update the desktop texture; this will block until there is some change
                        outputDuplication.AcquireNextFrame(1000, out var outputDuplicateFrameInformation, out IDXGIResource resource);
                        var texture = resource.QueryInterface<ID3D11Texture2D>();

                        // pick up the window under the cursor
                        var cursorPos = new POINT();
                        GetCursorPos(out cursorPos);
                        var hwnd = WindowFromPoint(cursorPos);
                        var rect = new RECT();
                        GetWindowRect(hwnd, out rect);

                        // adjust bounds so falls within source texture
                        if (rect.Left < 0) rect.Left = 0;
                        if (rect.Top < 0) rect.Top = 0;
                        if (rect.Right > (int)texture.Description.Width - 1) rect.Right = (int)texture.Description.Width;
                        if (rect.Bottom > (int)texture.Description.Height - 1) rect.Bottom = (int)texture.Description.Height;

                        int width = rect.Right - rect.Left;
                        int height = rect.Bottom - rect.Top;

                        // resize our texture if necessary
                        if ((desktopTexture == null) || ((int)desktopTexture.Description.Width != width) || ((int)desktopTexture.Description.Height != height))
                        {
                            if (desktopTexture != null)
                            {
                                desktopTextureSRV.Dispose();
                                desktopTexture.Dispose();
                            }
                            var desktopTextureDesc = new Texture2DDescription()
                            {
                                Width = (uint)width,
                                Height = (uint)height,
                                MipLevels = 1, // revisit this; we may benefit from mipmapping?
                                ArraySize = 1,
                                Format = Format.B8G8R8A8_UNorm,
                                SampleDescription = new SampleDescription(1, 0),
                                Usage = ResourceUsage.Default,
                                BindFlags = BindFlags.ShaderResource,
                                CPUAccessFlags = CpuAccessFlags.None,
                            };
                            desktopTexture = device.CreateTexture2D(desktopTextureDesc);
                            desktopTextureSRV = device.CreateShaderResourceView(desktopTexture);
                        }

                        // copy the window region into our texture
                        var sourceRegion = new Box()
                        {
                            Left = rect.Left,
                            Right = rect.Right,
                            Top = rect.Top,
                            Bottom = rect.Bottom,
                            Front = 0,
                            Back = 1,
                        };
                        deviceContext.CopySubresourceRegion(desktopTexture, 0, 0, 0, 0, texture, 0, sourceRegion);
                        texture.Dispose();
                    }

                    // render user view to seperate form
                    passThroughShader.viewport = new Viewport(0, 0, userViewForm.videoPanel1.Width, userViewForm.videoPanel1.Height);

                    // TODO: clean this up by simply using a pointer to the userViewSRV
                    if (threeDObjectEnabled)
                    {
                        passThroughShader.Render(deviceContext, userViewSRV, userViewForm.renderTargetView);
                    }
                    if (wobbleEffectEnabled)
                    {
                        passThroughShader.Render(deviceContext, filteredUserViewSRV, userViewForm.renderTargetView);
                    }
                    if (desktopDuplicationEnabled)
                    {
                        passThroughShader.Render(deviceContext, desktopTextureSRV, userViewForm.renderTargetView);
                    }
                    userViewForm.swapChain.Present(0, PresentFlags.None);

                    // projection puts x and y in [-1,1]; adjust to obtain texture coordinates [0,1]
                    // TODO: put this in SetContants?
                    userProjection[0, 0] /= 2;
                    userProjection[1, 1] /= -2; // y points down
                    userProjection[2, 0] += 0.5f;
                    userProjection[2, 1] += 0.5f;

                    // projection mapping for each projector
                    foreach (var form in projectorForms)
                    {
                        deviceContext.ClearRenderTargetView(form.renderTargetView, new Color4(0, 0, 0, 1));
                        deviceContext.ClearDepthStencilView(form.depthStencilView, DepthStencilClearFlags.Depth, 1, 0);

                        foreach (var camera in ensemble.cameras)
                        {
                            var cameraDeviceResource = cameraDeviceResources[camera];

                            var world = new Matrix4x4();
                            for (int i = 0; i < 4; i++)
                                for (int j = 0; j < 4; j++)
                                    world[i, j] = (float)camera.pose[i, j];
                            world = Matrix4x4.Transpose(world);

                            var projectorWorldViewProjection = world * form.view * form.projection;
                            var userWorldViewProjection = world * userView * userProjection;

                            projectiveTexturingShader.SetConstants(deviceContext, userWorldViewProjection, projectorWorldViewProjection);

                            // TODO: clean this up by simply using a pointer to the userViewSRV
                            if (wobbleEffectEnabled)
                                projectiveTexturingShader.Render(deviceContext, cameraDeviceResource.floatDepthImageRV, filteredUserViewSRV, cameraDeviceResource.vertexBuffer, form.renderTargetView, form.depthStencilView, form.viewport);
                            if (threeDObjectEnabled)
                                projectiveTexturingShader.Render(deviceContext, cameraDeviceResource.floatDepthImageRV, userViewSRV, cameraDeviceResource.vertexBuffer, form.renderTargetView, form.depthStencilView, form.viewport);
                            if (desktopDuplicationEnabled)
                                projectiveTexturingShader.Render(deviceContext, cameraDeviceResource.floatDepthImageRV, desktopTextureSRV, cameraDeviceResource.vertexBuffer, form.renderTargetView, form.depthStencilView, form.viewport);
                        }

                        form.swapChain.Present(1, PresentFlags.None);
                    }


                    if (desktopDuplicationEnabled)
                        outputDuplication.ReleaseFrame();


                    //Console.WriteLine(stopwatch.ElapsedMilliseconds);
                    stopwatch.Restart();
                }
            }
        }

        // Head tracking state (to be populated by IDepthSensor-based tracking)
        Vector3 trackedHeadPosition = new Vector3(0f, 0.3f, 1.5f);
        Vector3 handLeftPosition, handRightPosition;
        Object headPositionLock = new Object();
        bool trackingValid = false;




        class CameraDeviceResource : IDisposable
        {
            // encapsulates d3d resources for a camera
            public CameraDeviceResource(ID3D11Device device, ProjectorCameraEnsemble.Camera camera, Object renderLock, string directory)
            {
                this.device = device;
                this.camera = camera;
                this.renderLock = renderLock;

                // Kinect depth image
                var depthImageTextureDesc = new Texture2DDescription()
                {
                    Width = (uint)Kinect2Calibration.depthImageWidth,
                    Height = (uint)Kinect2Calibration.depthImageHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R16_UInt,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Dynamic,
                    BindFlags = BindFlags.ShaderResource,
                    CPUAccessFlags = CpuAccessFlags.Write,
                };
                depthImageTexture = device.CreateTexture2D(depthImageTextureDesc);
                depthImageTextureRV = device.CreateShaderResourceView(depthImageTexture);

                var floatDepthImageTextureDesc = new Texture2DDescription()
                {
                    Width = (uint)Kinect2Calibration.depthImageWidth,
                    Height = (uint)Kinect2Calibration.depthImageHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R32_Float,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                    CPUAccessFlags = CpuAccessFlags.None,
                };

                floatDepthImageTexture = device.CreateTexture2D(floatDepthImageTextureDesc);
                floatDepthImageRV = device.CreateShaderResourceView(floatDepthImageTexture);
                floatDepthImageRenderTargetView = device.CreateRenderTargetView(floatDepthImageTexture);

                floatDepthImageTexture2 = device.CreateTexture2D(floatDepthImageTextureDesc);
                floatDepthImageRV2 = device.CreateShaderResourceView(floatDepthImageTexture2);
                floatDepthImageRenderTargetView2 = device.CreateRenderTargetView(floatDepthImageTexture2);

                // Kinect color image
                var colorImageStagingTextureDesc = new Texture2DDescription()
                {
                    Width = (uint)Kinect2Calibration.colorImageWidth,
                    Height = (uint)Kinect2Calibration.colorImageHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Dynamic,
                    BindFlags = BindFlags.ShaderResource,
                    CPUAccessFlags = CpuAccessFlags.Write
                };
                colorImageStagingTexture = device.CreateTexture2D(colorImageStagingTextureDesc);

                var colorImageTextureDesc = new Texture2DDescription()
                {
                    Width = (uint)Kinect2Calibration.colorImageWidth,
                    Height = (uint)Kinect2Calibration.colorImageHeight,
                    MipLevels = 0,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                    CPUAccessFlags = CpuAccessFlags.None,
                    MiscFlags = ResourceOptionFlags.GenerateMips
                };
                colorImageTexture = device.CreateTexture2D(colorImageTextureDesc);
                colorImageTextureRV = device.CreateShaderResourceView(colorImageTexture);

                // vertex buffer
                var table = camera.calibration.ComputeDepthFrameToCameraSpaceTable();
                int numVertices = 6 * (Kinect2Calibration.depthImageWidth - 1) * (Kinect2Calibration.depthImageHeight - 1);
                var vertices = new VertexPosition[numVertices];

                Int3[] quadOffsets = new Int3[]
                {
                    new Int3(0, 0, 0),
                    new Int3(1, 0, 0),
                    new Int3(0, 1, 0),
                    new Int3(1, 0, 0),
                    new Int3(1, 1, 0),
                    new Int3(0, 1, 0),
                };

                int vertexIndex = 0;
                for (int y = 0; y < Kinect2Calibration.depthImageHeight - 1; y++)
                    for (int x = 0; x < Kinect2Calibration.depthImageWidth - 1; x++)
                        for (int i = 0; i < 6; i++)
                        {
                            int vertexX = x + quadOffsets[i].X;
                            int vertexY = y + quadOffsets[i].Y;

                            var point = table[Kinect2Calibration.depthImageWidth * vertexY + vertexX];

                            var vertex = new VertexPosition();
                            vertex.position = new Vector4(point.X, point.Y, vertexX, vertexY);
                            vertices[vertexIndex++] = vertex;
                        }

                var vertexBufferDesc = new BufferDescription()
                {
                    BindFlags = BindFlags.VertexBuffer,
                    CPUAccessFlags = CpuAccessFlags.None,
                    Usage = ResourceUsage.Default,
                    ByteWidth = (uint)(numVertices * VertexPosition.SizeInBytes),
                };
                vertexBuffer = device.CreateBuffer(vertices, vertexBufferDesc);

                var colorImage = new RoomAliveToolkit.ARGBImage(Kinect2Calibration.colorImageWidth, Kinect2Calibration.colorImageHeight);
                ProjectorCameraEnsemble.LoadFromTiff(colorImage, directory + "/camera" + camera.name + "/color.tiff");

                var depthImage = new RoomAliveToolkit.ShortImage(Kinect2Calibration.depthImageWidth, Kinect2Calibration.depthImageHeight);
                ProjectorCameraEnsemble.LoadFromTiff(depthImage, directory + "/camera" + camera.name + "/mean.tiff");

                lock (renderLock) // necessary?
                {
                    UpdateColorImage(device.ImmediateContext, colorImage.DataIntPtr);
                    UpdateDepthImage(device.ImmediateContext, depthImage.DataIntPtr);
                }

                colorImage.Dispose();
                depthImage.Dispose();
            }

            struct VertexPosition
            {
                public Vector4 position;
                static public int SizeInBytes { get { return 4 * 4; } }
            }

            public void Dispose()
            {
                depthImageTexture.Dispose();
                depthImageTextureRV.Dispose();
                colorImageTexture.Dispose();
                colorImageTextureRV.Dispose();
                colorImageStagingTexture.Dispose();
                vertexBuffer.Dispose();
            }

            ID3D11Device device;
            public ID3D11Texture2D depthImageTexture, floatDepthImageTexture, floatDepthImageTexture2;
            public ID3D11ShaderResourceView depthImageTextureRV, floatDepthImageRV, floatDepthImageRV2;
            public ID3D11RenderTargetView floatDepthImageRenderTargetView, floatDepthImageRenderTargetView2;
            public ID3D11Texture2D colorImageTexture;
            public ID3D11ShaderResourceView colorImageTextureRV;
            public ID3D11Texture2D colorImageStagingTexture;
            public ID3D11Buffer vertexBuffer;
            ProjectorCameraEnsemble.Camera camera;
            public bool renderEnabled = true;

            public void UpdateDepthImage(ID3D11DeviceContext deviceContext, IntPtr depthImage)
            {
                var mapped = deviceContext.Map(depthImageTexture, 0, MapMode.WriteDiscard);
                unsafe
                {
                    Buffer.MemoryCopy(depthImage.ToPointer(), mapped.DataPointer.ToPointer(),
                        mapped.RowPitch * Kinect2Calibration.depthImageHeight,
                        Kinect2Calibration.depthImageWidth * Kinect2Calibration.depthImageHeight * 2);
                }
                deviceContext.Unmap(depthImageTexture, 0);
            }

            public void UpdateDepthImage(ID3D11DeviceContext deviceContext, byte[] depthImage)
            {
                var mapped = deviceContext.Map(depthImageTexture, 0, MapMode.WriteDiscard);
                Marshal.Copy(depthImage, 0, mapped.DataPointer, Kinect2Calibration.depthImageWidth * Kinect2Calibration.depthImageHeight * 2);
                deviceContext.Unmap(depthImageTexture, 0);
            }

            public void UpdateColorImage(ID3D11DeviceContext deviceContext, IntPtr colorImage)
            {
                var mapped = deviceContext.Map(colorImageStagingTexture, 0, MapMode.WriteDiscard);
                unsafe
                {
                    Buffer.MemoryCopy(colorImage.ToPointer(), mapped.DataPointer.ToPointer(),
                        mapped.RowPitch * Kinect2Calibration.colorImageHeight,
                        Kinect2Calibration.colorImageWidth * Kinect2Calibration.colorImageHeight * 4);
                }
                deviceContext.Unmap(colorImageStagingTexture, 0);

                var resourceRegion = new Box()
                {
                    Left = 0,
                    Top = 0,
                    Right = Kinect2Calibration.colorImageWidth,
                    Bottom = Kinect2Calibration.colorImageHeight,
                    Front = 0,
                    Back = 1,
                };
                deviceContext.CopySubresourceRegion(colorImageTexture, 0, 0, 0, 0, colorImageStagingTexture, 0, resourceRegion);
                deviceContext.GenerateMips(colorImageTextureRV);
            }

            public void UpdateColorImage(ID3D11DeviceContext deviceContext, byte[] colorImage)
            {
                var mapped = deviceContext.Map(colorImageStagingTexture, 0, MapMode.WriteDiscard);
                Marshal.Copy(colorImage, 0, mapped.DataPointer, Kinect2Calibration.colorImageWidth * Kinect2Calibration.colorImageHeight * 4);
                deviceContext.Unmap(colorImageStagingTexture, 0);

                var resourceRegion = new Box()
                {
                    Left = 0,
                    Top = 0,
                    Right = Kinect2Calibration.colorImageWidth,
                    Bottom = Kinect2Calibration.colorImageHeight,
                    Front = 0,
                    Back = 1,
                };
                deviceContext.CopySubresourceRegion(colorImageTexture, 0, 0, 0, 0, colorImageStagingTexture, 0, resourceRegion);
                deviceContext.GenerateMips(colorImageTextureRV);
            }

            public void Render(ID3D11DeviceContext deviceContext)
            {
                deviceContext.IASetVertexBuffer(0, vertexBuffer, (uint)VertexPosition.SizeInBytes);
                deviceContext.VSSetShaderResource(0, depthImageTextureRV);
                deviceContext.PSSetShaderResource(0, colorImageTextureRV);
                deviceContext.Draw((Kinect2Calibration.depthImageWidth - 1) * (Kinect2Calibration.depthImageHeight - 1) * 6, 0);
            }

            public void StartLive()
            {
                new System.Threading.Thread(DepthCameraLoop).Start();
            }

            public void StopLive()
            {
            }


            Object renderLock;
            public bool depthImageChanged = true;

            byte[] nextColorData = new byte[4 * RoomAliveToolkit.Kinect2Calibration.colorImageWidth * RoomAliveToolkit.Kinect2Calibration.colorImageHeight];
            void ColorCameraLoop()
            {
                while (true)
                {
                    var encodedColorData = camera.Client.LatestJPEGImage();

                    // decode JPEG using System.Drawing
                    using (var memoryStream = new MemoryStream(encodedColorData))
                    using (var bitmap = new Bitmap(memoryStream))
                    {
                        var bitmapData = bitmap.LockBits(
                            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                            ImageLockMode.ReadOnly,
                            PixelFormat.Format32bppArgb);
                        Marshal.Copy(bitmapData.Scan0, nextColorData, 0,
                            Math.Min(nextColorData.Length, bitmapData.Stride * bitmapData.Height));
                        bitmap.UnlockBits(bitmapData);
                    }

                    lock (renderLock) // necessary?
                    {
                        UpdateColorImage(device.ImmediateContext, nextColorData);
                    }
                }
            }

            byte[] nextDepthData;
            void DepthCameraLoop()
            {
                while (true)
                {
                    nextDepthData = camera.Client.LatestDepthImage();
                    lock (renderLock)
                    {
                        depthImageChanged = true;
                        UpdateDepthImage(device.ImmediateContext, nextDepthData);
                    }
                }
            }

            static void Swap<T>(ref T first, ref T second)
            {
                T temp = first;
                first = second;
                second = temp;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x, y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetClientRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetCursorPos(out POINT lpPoint);
    }
}
