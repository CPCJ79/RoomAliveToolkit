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
    public partial class MainForm : Form
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }

        public MainForm(string[] args)
        {
            InitializeComponent();
            this.args = args;
        }

        ProjectorCameraEnsemble ensemble;
        string path, directory;
        string[] args;
        bool unsavedChanges = false;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // create d3d device and shaders
            var swapChainDesc = new SwapChainDescription
            {
                BufferCount = 1,
                BufferUsage = Usage.RenderTargetOutput,
                OutputWindow = videoPanel1.Handle,
                Windowed = true,
                BufferDescription = new ModeDescription(0, 0, new Rational(60, 1), Format.R8G8B8A8_UNorm),
                Flags = SwapChainFlags.AllowModeSwitch,
                SwapEffect = SwapEffect.Discard,
                SampleDescription = new SampleDescription(1, 0),
            };

            // When using DeviceCreationFlags.Debug on Windows 10, ensure that "Graphics Tools" are installed via Settings/System/Apps & features/Manage optional features.
            // Also, when debugging in VS, "Enable native code debugging" must be selected on the project.
            D3D11.D3D11CreateDeviceAndSwapChain(null, Vortice.Direct3D.DriverType.Hardware, DeviceCreationFlags.None, null, swapChainDesc, out swapChain, out device, out _, out _);

            // render target
            renderTarget = swapChain.GetBuffer<ID3D11Texture2D>(0);
            renderTargetView = device.CreateRenderTargetView(renderTarget);

            // depth buffer
            var depthBufferDesc = new Texture2DDescription()
            {
                Width = (uint)videoPanel1.Width,
                Height = (uint)videoPanel1.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.D32_Float, // necessary?
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.DepthStencil,
                CPUAccessFlags = CpuAccessFlags.None
            };
            depthStencil = device.CreateTexture2D(depthBufferDesc);
            depthStencilView = device.CreateDepthStencilView(depthStencil);

            // viewport
            viewport = new Viewport(0, 0, videoPanel1.Width, videoPanel1.Height, 0f, 1f);

            // shaders
            var shaderByteCode = File.ReadAllBytes("Content/DepthAndColorVS.cso");
            depthAndColorVS2 = device.CreateVertexShader(shaderByteCode);
            depthAndColorGS2 = device.CreateGeometryShader(File.ReadAllBytes("Content/DepthAndColorGS.cso"));
            depthAndColorPS2 = device.CreatePixelShader(File.ReadAllBytes("Content/DepthAndColorPS.cso"));

            // depth stencil state
            var depthStencilStateDesc = new DepthStencilDescription()
            {
                DepthEnable = true,
                DepthWriteMask = DepthWriteMask.All,
                DepthFunc = ComparisonFunction.LessEqual,
                StencilEnable = false,
            };
            depthStencilState = device.CreateDepthStencilState(depthStencilStateDesc);

            // rasterizer states
            var rasterizerStateDesc = new RasterizerDescription()
            {
                CullMode = CullMode.None, // beware what this does to both shaders
                FillMode = FillMode.Solid,
                DepthClipEnable = true,
                FrontCounterClockwise = true,
                MultisampleEnable = true,
            };
            rasterizerState = device.CreateRasterizerState(rasterizerStateDesc);

            // color sampler state
            var colorSamplerStateDesc = new SamplerDescription()
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Border,
                AddressV = TextureAddressMode.Border,
                AddressW = TextureAddressMode.Border,
                BorderColor = new Color4(0.5f, 0.5f, 0.5f, 1.0f),
                //BorderColor = new Color4(0, 0, 0, 1.0f),
            };
            colorSamplerState = device.CreateSamplerState(colorSamplerStateDesc);

            // constant buffer
            var constantBufferDesc = new BufferDescription()
            {
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                ByteWidth = (uint)ConstantBuffer.size,
                CPUAccessFlags = CpuAccessFlags.Write,
                StructureByteStride = 0,
                MiscFlags = 0,
            };
            constantBuffer = device.CreateBuffer(constantBufferDesc);

            // vertex layout is the same for all cameras
            vertexInputLayout = device.CreateInputLayout(new[]
            {
                new InputElementDescription("SV_POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
            }, shaderByteCode);

            manipulator = new Manipulator(videoPanel1);

            // disable most menu items when no file is loaded
            saveToolStripMenuItem.Enabled = false;
            saveAsToolStripMenuItem.Enabled = false;
            reloadToolStripMenuItem.Enabled = false;
            calibrateToolStripMenuItem.Enabled = false;
            renderToolStripMenuItem.Enabled = false;
            viewToolStripMenuItem.Enabled = false;
            displayProjectorDisplayIndexesToolStripMenuItem.Enabled = false;
            displayProjectorNamesToolStripMenuItem.Enabled = false;

            if (args.Length > 0)
            {
                path = args[0];
                directory = Path.GetDirectoryName(path);
                LoadEnsemble();
            }

            Width = Properties.Settings.Default.FormWidth;
            Height = Properties.Settings.Default.FormHeight;
            splitContainer1.SplitterDistance = Properties.Settings.Default.SplitterDistance;

            new System.Threading.Thread(RenderLoop).Start();
        }

        ID3D11Device device;
        IDXGISwapChain swapChain;
        ID3D11Texture2D renderTarget, depthStencil;
        ID3D11RenderTargetView renderTargetView;
        ID3D11DepthStencilView depthStencilView;
        Viewport viewport;
        ID3D11VertexShader depthAndColorVS2;
        ID3D11GeometryShader depthAndColorGS2;
        ID3D11PixelShader depthAndColorPS2;
        ID3D11DepthStencilState depthStencilState;
        ID3D11RasterizerState rasterizerState;
        ID3D11SamplerState colorSamplerState;
        ID3D11InputLayout vertexInputLayout;
        ID3D11Buffer constantBuffer;
        Manipulator manipulator;



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
                    Width = Kinect2Calibration.depthImageWidth,
                    Height = Kinect2Calibration.depthImageHeight,
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

                // Kinect color image
                var colorImageStagingTextureDesc = new Texture2DDescription()
                {
                    Width = Kinect2Calibration.colorImageWidth,
                    Height = Kinect2Calibration.colorImageHeight,
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
                    Width = Kinect2Calibration.colorImageWidth,
                    Height = Kinect2Calibration.colorImageHeight,
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

                if (File.Exists(directory + "/camera" + camera.name + "/color.tiff")) // FIX: this assumes that mean.tiff is exists (very likely)
                {
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

                //StartLive();

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
            public ID3D11Texture2D depthImageTexture;
            public ID3D11ShaderResourceView depthImageTextureRV;
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

            bool live = false;

            public void StartLive()
            {
                live = true;
                new System.Threading.Thread(ColorCameraLoop).Start();
                new System.Threading.Thread(DepthCameraLoop).Start();
            }

            public void StopLive()
            {
                live = false;
            }

            Object renderLock;

            byte[] nextColorData = new byte[4 * RoomAliveToolkit.Kinect2Calibration.colorImageWidth * RoomAliveToolkit.Kinect2Calibration.colorImageHeight];
            void ColorCameraLoop()
            {
                while (live)
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
                while (live)
                {
                    nextDepthData = camera.Client.LatestDepthImage();
                    lock (renderLock)
                    {
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

        // protip: compile shader with /Fc; output gives exact layout
        // hlsl matrices are stored column major
        // variables are stored on 4-component boundaries; inc. matrix columns
        // size is a multiple of 16
        [StructLayout(LayoutKind.Explicit, Size = ConstantBuffer.size)]
        unsafe struct ConstantBuffer
        {
            public const int size = 160;

            [FieldOffset(0)]
            public fixed float depthToColorTransform[16]; // 4-component padding
            [FieldOffset(64)]
            public fixed float f[2];
            [FieldOffset(72)]
            public fixed float c[2];
            [FieldOffset(80)]
            public float k1;
            [FieldOffset(84)]
            public float k2;
            [FieldOffset(96)]
            public fixed float projection[16];
        };

        public unsafe void SetConstants(ID3D11DeviceContext deviceContext, RoomAliveToolkit.Kinect2Calibration kinect2Calibration, Matrix4x4 projection)
        {
            // hlsl matrices are default column order
            var constants = new ConstantBuffer();
            for (int i = 0, col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                {
                    constants.projection[i] = projection[row, col];
                    constants.depthToColorTransform[i] = (float)kinect2Calibration.depthToColorTransform[row, col];
                    i++;
                }
            constants.f[0] = (float)kinect2Calibration.colorCameraMatrix[0, 0];
            constants.f[1] = (float)kinect2Calibration.colorCameraMatrix[1, 1];
            constants.c[0] = (float)kinect2Calibration.colorCameraMatrix[0, 2];
            constants.c[1] = (float)kinect2Calibration.colorCameraMatrix[1, 2];
            constants.k1 = (float)kinect2Calibration.colorLensDistortion[0];
            constants.k2 = (float)kinect2Calibration.colorLensDistortion[1];

            var mapped = deviceContext.Map(constantBuffer, MapMode.WriteDiscard);
            Marshal.StructureToPtr(constants, mapped.DataPointer, false);
            deviceContext.Unmap(constantBuffer, 0);
        }

        Object renderLock = new Object();


        void RenderLoop()
        {
            while (true)
            {
                lock (renderLock)
                {
                    view = manipulator.Update();

                    var deviceContext = device.ImmediateContext;

                    deviceContext.IASetInputLayout(vertexInputLayout);
                    deviceContext.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
                    deviceContext.OMSetRenderTargets(renderTargetView, depthStencilView);
                    deviceContext.OMSetDepthStencilState(depthStencilState);
                    deviceContext.RSSetState(rasterizerState);
                    deviceContext.RSSetViewport(viewport);
                    deviceContext.VSSetShader(depthAndColorVS2);
                    deviceContext.VSSetConstantBuffer(0, constantBuffer);
                    deviceContext.GSSetShader(depthAndColorGS2);
                    deviceContext.PSSetShader(depthAndColorPS2);
                    deviceContext.PSSetSampler(0, colorSamplerState);
                    deviceContext.ClearRenderTargetView(renderTargetView, new Color4(0, 0, 0, 1));
                    deviceContext.ClearDepthStencilView(depthStencilView, DepthStencilClearFlags.Depth, 1, 0);

                    // render all cameras
                    if (ensemble != null)
                        foreach (var camera in ensemble.cameras)
                        {
                            if (cameraDeviceResources.ContainsKey(camera))
                                if (cameraDeviceResources[camera].renderEnabled && (camera.pose != null))
                                {
                                    var world = new Matrix4x4();
                                    for (int i = 0; i < 4; i++)
                                        for (int j = 0; j < 4; j++)
                                            world[i, j] = (float)camera.pose[i, j];
                                    world = Matrix4x4.Transpose(world);

                                    // view and projection matrix are post-multiply
                                    var worldViewProjection = world * view * projection;

                                    SetConstants(deviceContext, camera.calibration, worldViewProjection);
                                    cameraDeviceResources[camera].Render(deviceContext);
                                }
                        }

                    swapChain.Present(1, PresentFlags.None);
                }
            }
        }

        Matrix4x4 view, projection;

        Dictionary<ProjectorCameraEnsemble.Camera, CameraDeviceResource> cameraDeviceResources = new Dictionary<ProjectorCameraEnsemble.Camera, CameraDeviceResource>();

        void EnsembleChanged()
        {
            lock (renderLock)
            {
                // deallocate/allocate camera d3d resources
                foreach (var cameraDeviceResource in cameraDeviceResources.Values)
                    cameraDeviceResource.Dispose();
                cameraDeviceResources.Clear();

                foreach (var camera in ensemble.cameras)
                {
                    if (camera.calibration != null) // TODO: this might not be the right way to check
                        cameraDeviceResources[camera] = new CameraDeviceResource(device, camera, renderLock, directory);
                }
            }

            Invoke((Action)delegate
            {
                // build Render menu
                foreach (var menuItem in cameraMenuItems)
                    renderToolStripMenuItem.DropDownItems.Remove(menuItem);
                foreach (var camera in ensemble.cameras)
                {
                    var toolStripMenuItem = new ToolStripMenuItem("Camera " + camera.name, null, renderMenuItem_Click);
                    toolStripMenuItem.Tag = camera;
                    toolStripMenuItem.Checked = true;
                    renderToolStripMenuItem.DropDownItems.Add(toolStripMenuItem);
                    cameraMenuItems.Add(toolStripMenuItem);
                }

                // build View menu
                foreach (var menuItem in projectorMenuItems)
                    viewToolStripMenuItem.DropDownItems.Remove(menuItem);
                foreach (var projector in ensemble.projectors)
                {
                    var toolStripMenuItem = new ToolStripMenuItem("Projector " + projector.name, null, viewMenuItem_Click);
                    toolStripMenuItem.Tag = projector;
                    toolStripMenuItem.Checked = false;
                    viewToolStripMenuItem.DropDownItems.Add(toolStripMenuItem);
                    projectorMenuItems.Add(toolStripMenuItem);
                }

                SetDefaultView();

                // we have a file loaded, so enable menu items
                saveToolStripMenuItem.Enabled = true;
                saveAsToolStripMenuItem.Enabled = true;
                reloadToolStripMenuItem.Enabled = true;
                calibrateToolStripMenuItem.Enabled = true;
                renderToolStripMenuItem.Enabled = true;
                viewToolStripMenuItem.Enabled = true;
                displayProjectorDisplayIndexesToolStripMenuItem.Enabled = true;
                displayProjectorNamesToolStripMenuItem.Enabled = true;

                Text = Path.GetFileName(path) + " - CalibrateEnsemble";
            });

        }

        List<ToolStripMenuItem> cameraMenuItems = new List<ToolStripMenuItem>();
        List<ToolStripMenuItem> projectorMenuItems = new List<ToolStripMenuItem>();


        #region Form event handlers

        void renderMenuItem_Click(object sender, EventArgs e)
        {
            var toolStripMenuItem = (ToolStripMenuItem)sender;
            var camera = (ProjectorCameraEnsemble.Camera)toolStripMenuItem.Tag;
            toolStripMenuItem.Checked = !toolStripMenuItem.Checked;
            cameraDeviceResources[camera].renderEnabled = toolStripMenuItem.Checked;
        }

        bool perspectiveView;

        private void perspectiveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (var menuItem in projectorMenuItems)
                menuItem.Checked = false;
            SetDefaultView();
        }

        void viewMenuItem_Click(object sender, EventArgs e)
        {
            var toolStripMenuItem = (ToolStripMenuItem)sender;

            perspectiveAtOriginToolStripMenuItem.Checked = false;
            foreach (var menuItem in projectorMenuItems)
                menuItem.Checked = false;

            toolStripMenuItem.Checked = true;

            var projector = (ProjectorCameraEnsemble.Projector)toolStripMenuItem.Tag;
            SetViewProjectionFromProjector(projector);
            manipulator.View = view;
            manipulator.OriginalView = view;
            perspectiveView = false;
        }

        private void acquireToolStripMenuItem_Click(object sender, EventArgs e)
        {
            calibrateToolStripMenuItem.Enabled = false;
            new System.Threading.Thread(Acquire).Start();
        }

        private void solveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            calibrateToolStripMenuItem.Enabled = false;
            new System.Threading.Thread(Solve).Start();
        }

        private void acquireDepthAndColorOnlyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            calibrateToolStripMenuItem.Enabled = false;
            new System.Threading.Thread(AcquireDepthAndColor).Start();
        }

        private void decodeGrayCodesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            calibrateToolStripMenuItem.Enabled = false;
            new System.Threading.Thread(DecodeGrayCodes).Start();
        }

        private void calibrateProjectorGroupsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            calibrateToolStripMenuItem.Enabled = false;
            new System.Threading.Thread(CalibrateProjectorGroups).Start();
        }

        private void optimizePoseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            calibrateToolStripMenuItem.Enabled = false;
            new System.Threading.Thread(OptimizePose).Start();
        }

        private void newToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var newDialog = new NewDialog();
            if (newDialog.ShowDialog(this) == DialogResult.OK)
            {
                var newEnsemble = new ProjectorCameraEnsemble(newDialog.NumProjectors, newDialog.NumCameras);

                var saveFileDialog = new SaveFileDialog();

                saveFileDialog.Filter = "xml files (*.xml)|*.xml|All files (*.*)|*.*";
                saveFileDialog.FilterIndex = 0;
                saveFileDialog.RestoreDirectory = true;

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        path = saveFileDialog.FileName;
                        directory = Path.GetDirectoryName(path);
                        newEnsemble.Save(path);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Could not save file to disk.\n" + ex);
                        return;
                    }
                    unsavedChanges = false;
                    lock (renderLock)
                    {
                        ensemble = newEnsemble;
                        EnsembleChanged();
                    }
                }
            }
        }

        private void reloadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            LoadEnsemble();
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                ensemble.Save(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not save file to disk.\n" + ex);
                return;
            }
            unsavedChanges = false;
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "xml files (*.xml)|*.xml|All files (*.*)|*.*";
            openFileDialog.FilterIndex = 0;
            openFileDialog.RestoreDirectory = true;

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                path = openFileDialog.FileName;
                directory = Path.GetDirectoryName(path);
                LoadEnsemble();
            }
        }

        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var saveFileDialog = new SaveFileDialog();

            saveFileDialog.Filter = "xml files (*.xml)|*.xml|All files (*.*)|*.*";
            saveFileDialog.FilterIndex = 0;
            saveFileDialog.RestoreDirectory = true;

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    path = saveFileDialog.FileName;
                    directory = Path.GetDirectoryName(path);
                    ensemble.Save(path);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Could not save file to disk.\n" + ex);
                    return;
                }
                unsavedChanges = false;
            }
        }

        private void saveToOBJToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var saveFileDialog = new SaveFileDialog();

            saveFileDialog.Filter = "obj files (*.obj)|*.obj|All files (*.*)|*.*";
            saveFileDialog.FilterIndex = 0;
            saveFileDialog.RestoreDirectory = true;

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    ensemble.SaveToOBJ(directory, saveFileDialog.FileName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Could not save file to disk.\n" + ex);
                }
            }
        }

        private void discoverCamerasToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // WCF discovery removed; configure cameras via ensemble XML instead
            Console.WriteLine("Camera discovery via WCF has been removed. Configure cameras in the ensemble XML file.");
        }

        private void discoverProjectorsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // WCF discovery removed; configure projectors via ensemble XML instead
            Console.WriteLine("Projector discovery via WCF has been removed. Configure projectors in the ensemble XML file.");
        }

        private void displayProjectorDisplayIndexesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (showingDisplayIndexes)
            {
                try
                {
                    HideDisplayIndexes();
                    displayProjectorDisplayIndexesToolStripMenuItem.Text = "Show Projector Server Connected Displays";
                }
                catch (Exception)
                {
                    Console.WriteLine("HideDisplayIndexes failed");
                }
            }
            else
            {
                try
                {
                    ShowDisplayIndexes();
                    displayProjectorDisplayIndexesToolStripMenuItem.Text = "Hide Projector Server Connected Displays";
                }
                catch (Exception)
                {
                    Console.WriteLine("ShowDisplayIndexes failed");
                }
            }
        }

        private void displayProjectorNamesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (showingDisplayNames)
            {
                try
                {
                    HideDisplayNames();
                    displayProjectorNamesToolStripMenuItem.Text = "Show Projector Names";
                }
                catch (Exception)
                {
                    Console.WriteLine("HideProjectorNames failed");
                }
            }
            else
            {
                try
                {
                    ShowDisplayNames();
                    displayProjectorNamesToolStripMenuItem.Text = "Hide Projector Names";
                }
                catch (Exception)
                {
                    Console.WriteLine("ShowProjectorNames failed");
                }
            }
        }

        bool live = false;

        private void liveViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            live = !live;
            if (live)
                foreach (var cameraDeviceResource in cameraDeviceResources.Values)
                    cameraDeviceResource.StartLive();
            else
                foreach (var cameraDeviceResource in cameraDeviceResources.Values)
                    cameraDeviceResource.StopLive();
            liveViewToolStripMenuItem.Checked = live;
        }

        private void videoPanel1_SizeChanged(object sender, EventArgs e)
        {
            // TODO: look into using this as initial creation
            if (renderTargetView != null)
                lock (renderLock)
                {
                    renderTargetView.Dispose();
                    renderTarget.Dispose();

                    depthStencilView.Dispose();
                    depthStencil.Dispose();

                    swapChain.ResizeBuffers(1, (uint)videoPanel1.Width, (uint)videoPanel1.Height, Format.Unknown, SwapChainFlags.AllowModeSwitch);

                    renderTarget = swapChain.GetBuffer<ID3D11Texture2D>(0);
                    renderTargetView = device.CreateRenderTargetView(renderTarget);

                    // depth buffer
                    var depthBufferDesc = new Texture2DDescription()
                    {
                        Width = (uint)videoPanel1.Width,
                        Height = (uint)videoPanel1.Height,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.D32_Float, // necessary?
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Default,
                        BindFlags = BindFlags.DepthStencil,
                        CPUAccessFlags = CpuAccessFlags.None
                    };
                    depthStencil = device.CreateTexture2D(depthBufferDesc);
                    depthStencilView = device.CreateDepthStencilView(depthStencil);

                    // viewport
                    viewport = new Viewport(0, 0, videoPanel1.Width, videoPanel1.Height, 0f, 1f);
                    manipulator.Viewport = viewport;

                    // in the case of perspective projection, change projection to follow change in aspect
                    if (perspectiveView)
                    {
                        SetDefaultView();
                    }
                }
        }


        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!Exit())
                e.Cancel = true;
        }


        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Exit();
        }

        void SaveSettings()
        {
            Properties.Settings.Default.FormWidth = Width;
            Properties.Settings.Default.FormHeight = Height;
            Properties.Settings.Default.SplitterDistance = splitContainer1.SplitterDistance;
            Properties.Settings.Default.Save();
        }

        bool Exit()
        {
            if (unsavedChanges && (ensemble != null))
            {
                var unsavedChangesDialog = new UnsavedChangesDialog(Path.GetFileNameWithoutExtension(path));
                var result = unsavedChangesDialog.ShowDialog();
                switch (result)
                {
                    case DialogResult.Yes: // save
                        try
                        {
                            ensemble.Save(path);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Could not save file to disk.\n" + ex);
                            return false;
                        }
                        break;
                    case DialogResult.No: // don't save
                        break;
                    case DialogResult.Cancel:
                        return false;
                }
            }
            SaveSettings();
            Environment.Exit(0);
            return true;
        }
        #endregion



        bool showingDisplayIndexes = false;

        void ShowDisplayIndexes()
        {
            foreach (var projector in ensemble.projectors)
            {
                int screenCount = projector.Client.ScreenCount();
                for (int i = 0; i < screenCount; i++)
                {
                    projector.Client.OpenDisplay(i);
                    projector.Client.DisplayName(i, projector.hostNameOrAddress + ":" + i);
                }
            }
            showingDisplayIndexes = true;
        }

        void HideDisplayIndexes()
        {
            foreach (var projector in ensemble.projectors)
            {
                int screenCount = projector.Client.ScreenCount();
                for (int i = 0; i < screenCount; i++)
                    projector.Client.CloseDisplay(i);
            }
            showingDisplayIndexes = false;
        }

        bool showingDisplayNames = false;

        void ShowDisplayNames()
        {
            foreach (var projector in ensemble.projectors)
            {
                projector.Client.OpenDisplay(projector.displayIndex);
                projector.Client.DisplayName(projector.displayIndex, projector.name);
            }
            showingDisplayNames = true;
        }

        void HideDisplayNames()
        {
            foreach (var projector in ensemble.projectors)
                projector.Client.CloseDisplay(projector.displayIndex);
            showingDisplayNames = false;
        }

        void LoadEnsemble()
        {
            lock (renderLock)
            {
                try
                {
                    ensemble = ProjectorCameraEnsemble.FromFile(path);
                    Console.WriteLine("Loaded " + path);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Could not read file from disk.\n" + ex);
                    return;
                }
                EnsembleChanged();
                unsavedChanges = false;
            }
        }

        void Acquire()
        {
            try
            {
                ensemble.CaptureGrayCodes(directory);
                EnsembleChanged();
            }
            catch (Exception e)
            {
                Console.WriteLine("Acquire failed\n" + e);
            }
            Console.WriteLine("Acquire complete");
            unsavedChanges = true;
            Invoke((Action)delegate { calibrateToolStripMenuItem.Enabled = true; });
        }


        void Solve()
        {
            ensemble.DecodeGrayCodeImages(directory);
            try
            {
                ensemble.CalibrateProjectorGroups(directory);
                ensemble.OptimizePose();
            }
            catch (Exception e)
            {
                Console.WriteLine("Solve failed\n" + e);
            }
            Console.WriteLine("Solve complete");
            unsavedChanges = true;
            Invoke((Action)delegate { calibrateToolStripMenuItem.Enabled = true; });
        }

        void DecodeGrayCodes()
        {
            ensemble.DecodeGrayCodeImages(directory);
            Invoke((Action)delegate { calibrateToolStripMenuItem.Enabled = true; });
        }

        void CalibrateProjectorGroups()
        {
            try
            {
                ensemble.CalibrateProjectorGroups(directory);
            }
            catch (Exception e)
            {
                Console.WriteLine("Solve failed\n" + e);
            }
            Console.WriteLine("Solve complete");
            unsavedChanges = true;
            Invoke((Action)delegate { calibrateToolStripMenuItem.Enabled = true; });
        }

        void OptimizePose()
        {
            try
            {
                ensemble.OptimizePose();
            }
            catch (Exception e)
            {
                Console.WriteLine("Solve failed\n" + e);
            }
            Console.WriteLine("Solve complete");
            unsavedChanges = true;
            Invoke((Action)delegate { calibrateToolStripMenuItem.Enabled = true; });
        }

        void AcquireDepthAndColor()
        {
            try
            {
                ensemble.CaptureDepthAndColor(directory);
                EnsembleChanged();
            }
            catch (Exception e)
            {
                Console.WriteLine("Acquire Depth and Color failed\n" + e);
            }
            Console.WriteLine("Acquire Depth and Color complete");
            Invoke((Action)delegate { calibrateToolStripMenuItem.Enabled = true; });
        }


        // could be method on Projector:
        void SetViewProjectionFromProjector(ProjectorCameraEnsemble.Projector projector)
        {
            if ((projector.pose == null) || (projector.cameraMatrix == null))
                Console.WriteLine("Projector pose/camera matrix not set. Please perform a calibration.");
            else
            {
                // pick up view and projection for a given projector
                view = new Matrix4x4();
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 4; j++)
                        view[i, j] = (float)projector.pose[i, j];
                Matrix4x4.Invert(view, out view);
                view = Matrix4x4.Transpose(view);

                var cameraMatrix = projector.cameraMatrix;
                float fx = (float)cameraMatrix[0, 0];
                float fy = (float)cameraMatrix[1, 1];
                float cx = (float)cameraMatrix[0, 2];
                float cy = (float)cameraMatrix[1, 2];

                float near = 0.1f;
                float far = 100.0f;

                float w = projector.width;
                float h = projector.height;

                projection = GraphicsTransforms.ProjectionMatrixFromCameraMatrix(fx, fy, cx, cy, w, h, near, far);
                projection = Matrix4x4.Transpose(projection);
            }
        }

        void SetViewProjectionFromCamera(ProjectorCameraEnsemble.Camera camera)
        {
            if ((camera.pose == null) || (camera.calibration.colorCameraMatrix == null))
                Console.WriteLine("Camera pose not set.");
            else
            {
                view = new Matrix4x4();
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 4; j++)
                        view[i, j] = (float)camera.pose[i, j];
                Matrix4x4.Invert(view, out view);
                view = Matrix4x4.Transpose(view);

                var cameraMatrix = camera.calibration.colorCameraMatrix;
                float fx = (float)cameraMatrix[0, 0];
                float fy = (float)cameraMatrix[1, 1];
                float cx = (float)cameraMatrix[0, 2];
                float cy = (float)cameraMatrix[1, 2];

                float near = 0.1f;
                float far = 100.0f;

                float w = Kinect2Calibration.colorImageWidth;
                float h = Kinect2Calibration.colorImageHeight;

                projection = GraphicsTransforms.ProjectionMatrixFromCameraMatrix(fx, fy, cx, cy, w, h, near, far);
                projection = Matrix4x4.Transpose(projection);
            }
        }

        void SetDefaultView()
        {
            view = Matrix4x4.Identity;
            float aspect = (float)videoPanel1.Width / (float)videoPanel1.Height;
            projection = GraphicsTransforms.PerspectiveFov(35.0f / 180.0f * (float)Math.PI, aspect, 0.1f, 100.0f);
            projection = Matrix4x4.Transpose(projection);

            manipulator.View = view;
            manipulator.Projection = projection;
            manipulator.Viewport = viewport;
            manipulator.OriginalView = view;
            perspectiveAtOriginToolStripMenuItem.Checked = true;
            perspectiveView = true;
        }

    }
}
