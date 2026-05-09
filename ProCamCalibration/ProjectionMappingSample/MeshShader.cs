using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using System.Numerics;

namespace RoomAliveToolkit
{

    public class PointLight
    {
        public PointLight()
        {
            Ia = Vector3.One;
            Id = Vector3.One;
            Is = Vector3.One;
        }
        public Vector3 position;
        public Vector3 Ia, Id, Is;
    }

    public class MeshDeviceResources
    {
        public MeshDeviceResources(ID3D11Device device, Mesh mesh)
        {
            this.mesh = mesh;

            // create single vertex buffer
            var vertices = mesh.vertices.ToArray();

            var vertexBufferDesc = new BufferDescription()
            {
                BindFlags = BindFlags.VertexBuffer,
                CPUAccessFlags = CpuAccessFlags.None,
                Usage = ResourceUsage.Default,
                ByteWidth = (uint)(mesh.vertices.Count * Mesh.VertexPositionNormalTexture.sizeInBytes),
            };
            vertexBuffer = device.CreateBuffer(vertices, vertexBufferDesc);

            foreach (var subset in mesh.subsets)
            {
                if (subset.material.textureFilename != null)
                {
                    using (var bitmap = new Bitmap(subset.material.textureFilename))
                    {
                        var bitmapData = bitmap.LockBits(
                            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                            ImageLockMode.ReadOnly,
                            PixelFormat.Format32bppArgb);

                        var stagingTextureDesc = new Texture2DDescription()
                        {
                            Width = (uint)bitmap.Width,
                            Height = (uint)bitmap.Height,
                            MipLevels = 1,
                            ArraySize = 1,
                            Format = Format.B8G8R8A8_UNorm,
                            SampleDescription = new SampleDescription(1, 0),
                            Usage = ResourceUsage.Dynamic,
                            BindFlags = BindFlags.ShaderResource,
                            CPUAccessFlags = CpuAccessFlags.Write
                        };
                        var stagingTexture = device.CreateTexture2D(stagingTextureDesc);

                        var textureDesc = new Texture2DDescription()
                        {
                            Width = (uint)bitmap.Width,
                            Height = (uint)bitmap.Height,
                            MipLevels = 0,
                            ArraySize = 1,
                            Format = Format.B8G8R8A8_UNorm,
                            SampleDescription = new SampleDescription(1, 0),
                            Usage = ResourceUsage.Default,
                            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                            CPUAccessFlags = CpuAccessFlags.None,
                            MiscFlags = ResourceOptionFlags.GenerateMips
                        };
                        var texture = device.CreateTexture2D(textureDesc);

                        var mapped = device.ImmediateContext.Map(stagingTexture, 0, MapMode.WriteDiscard);
                        // Copy row by row in case pitches differ
                        for (int row = 0; row < bitmap.Height; row++)
                        {
                            var srcPtr = IntPtr.Add(bitmapData.Scan0, row * bitmapData.Stride);
                            var dstPtr = IntPtr.Add(mapped.DataPointer, (int)(row * mapped.RowPitch));
                            int bytesPerRow = bitmap.Width * 4;
                            unsafe
                            {
                                Buffer.MemoryCopy(srcPtr.ToPointer(), dstPtr.ToPointer(), bytesPerRow, bytesPerRow);
                            }
                        }
                        device.ImmediateContext.Unmap(stagingTexture, 0);

                        bitmap.UnlockBits(bitmapData);

                        var resourceRegion = new Box()
                        {
                            Left = 0,
                            Top = 0,
                            Right = bitmap.Width,
                            Bottom = bitmap.Height,
                            Front = 0,
                            Back = 1,
                        };
                        device.ImmediateContext.CopySubresourceRegion(texture, 0, 0, 0, 0, stagingTexture, 0, resourceRegion);
                        var textureRV = device.CreateShaderResourceView(texture);
                        device.ImmediateContext.GenerateMips(textureRV);

                        stagingTexture.Dispose();

                        textureRVs[subset] = textureRV;
                    }
                }
            }
        }


        public Mesh mesh;
        public ID3D11Buffer vertexBuffer;
        public Dictionary<Mesh.Subset, ID3D11ShaderResourceView> textureRVs = new Dictionary<Mesh.Subset, ID3D11ShaderResourceView>();
    }

    public class MeshShader
    {
        public MeshShader(ID3D11Device device)
        {
            shaderByteCode = File.ReadAllBytes("Content/MeshVS.cso");
            meshVS = device.CreateVertexShader(shaderByteCode);
            meshPS = device.CreatePixelShader(File.ReadAllBytes("Content/MeshPS.cso"));
            meshWithTexturePS = device.CreatePixelShader(File.ReadAllBytes("Content/MeshWithTexturePS.cso"));

            // depth stencil state
            var depthStencilStateDesc = new DepthStencilDescription()
            {
                DepthEnable = true,
                DepthWriteMask = DepthWriteMask.All,
                DepthFunc = ComparisonFunction.LessEqual,
                StencilEnable = false,
            };
            depthStencilState = device.CreateDepthStencilState(depthStencilStateDesc);

            // rasterizer state
            var rasterizerStateDesc = new RasterizerDescription()
            {
                CullMode = CullMode.None,
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
                AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap,
                AddressW = TextureAddressMode.Wrap,
            };
            colorSamplerState = device.CreateSamplerState(colorSamplerStateDesc);

            // constant buffer
            var VSConstantBufferDesc = new BufferDescription()
            {
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                ByteWidth = (uint)VSConstantBuffer.size,
                CPUAccessFlags = CpuAccessFlags.Write,
                StructureByteStride = 0,
                MiscFlags = 0,
            };
            vertexShaderConstantBuffer = device.CreateBuffer(VSConstantBufferDesc);

            var PSConstantBufferDesc = new BufferDescription()
            {
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                ByteWidth = (uint)PSConstantBuffer.size,
                CPUAccessFlags = CpuAccessFlags.Write,
                StructureByteStride = 0,
                MiscFlags = 0,
            };
            pixelShaderConstantBuffer = device.CreateBuffer(PSConstantBufferDesc);

            vertexInputLayout = device.CreateInputLayout(new[]
            {
                new InputElementDescription("SV_POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 16, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 24, 0),
            }, shaderByteCode);

        }

        // protip: compile shader with /Fc; output gives exact layout
        // hlsl matrices are stored column major
        // variables are stored on 4-component boundaries; inc. matrix columns
        // size is a multiple of 16
        [StructLayout(LayoutKind.Explicit, Size = VSConstantBuffer.size)]
        unsafe struct VSConstantBuffer
        {
            public const int size = 128 + 16;

            [FieldOffset(0)]
            public fixed float world[16]; // 4-component padding
            [FieldOffset(64)]
            public fixed float viewProjection[16]; // 4-component padding
            [FieldOffset(128)]
            public fixed float lightPosition[3];
        };

        public unsafe void SetVertexShaderConstants(ID3D11DeviceContext deviceContext, Matrix4x4 world, Matrix4x4 viewProjection, Vector3 lightPosition)
        {
            // hlsl matrices are default column order
            var constants = new VSConstantBuffer();
            for (int i = 0, col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                {
                    constants.world[i] = world[row, col];
                    constants.viewProjection[i] = viewProjection[row, col];
                    i++;
                }

            for (int i = 0; i < 3; i++)
                constants.lightPosition[i] = lightPosition[i];

            var mapped = deviceContext.Map(vertexShaderConstantBuffer, MapMode.WriteDiscard);
            Marshal.StructureToPtr(constants, mapped.DataPointer, false);
            deviceContext.Unmap(vertexShaderConstantBuffer, 0);
        }

        [StructLayout(LayoutKind.Explicit, Size = VSConstantBuffer.size)]
        unsafe struct PSConstantBuffer
        {
            public const int size = 108 + 4;

            [FieldOffset(0)]
            public fixed float cameraPosition[3];
            [FieldOffset(16)]
            public fixed float Ia[3];
            [FieldOffset(32)]
            public fixed float Id[3];
            [FieldOffset(48)]
            public fixed float Is[3];
            [FieldOffset(64)]
            public fixed float Ka[3];
            [FieldOffset(80)]
            public fixed float Kd[3];
            [FieldOffset(96)]
            public fixed float Ks[3];
            [FieldOffset(108)]
            public float Ns;
        };

        public unsafe void SetPixelShaderConstants(ID3D11DeviceContext deviceContext, Mesh.Material material, PointLight light)
        {
            // hlsl matrices are default column order
            var constants = new PSConstantBuffer();
            for (int i = 0; i < 3; i++)
            {
                constants.Ka[i] = material.ambientColor[i];
                constants.Kd[i] = material.diffuseColor[i];
                constants.Ks[i] = material.specularColor[i];

                constants.Ia[i] = light.Ia[i];
                constants.Id[i] = light.Id[i];
                constants.Is[i] = light.Is[i];
            }
            constants.Ns = material.shininess;

            // TODO: add camera position

            var mapped = deviceContext.Map(pixelShaderConstantBuffer, MapMode.WriteDiscard);
            Marshal.StructureToPtr(constants, mapped.DataPointer, false);
            deviceContext.Unmap(pixelShaderConstantBuffer, 0);
        }

        public void Render(ID3D11DeviceContext deviceContext, MeshDeviceResources meshDeviceResources, PointLight pointLight, ID3D11RenderTargetView renderTargetView, ID3D11DepthStencilView depthStencilView, Viewport viewport)
        {
            deviceContext.IASetInputLayout(vertexInputLayout);
            deviceContext.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
            deviceContext.IASetVertexBuffer(0, meshDeviceResources.vertexBuffer, (uint)Mesh.VertexPositionNormalTexture.sizeInBytes);
            deviceContext.RSSetState(rasterizerState);
            deviceContext.RSSetViewport(viewport);
            deviceContext.VSSetShader(meshVS);
            deviceContext.VSSetConstantBuffer(0, vertexShaderConstantBuffer);
            deviceContext.GSSetShader(null);
            deviceContext.PSSetShader(meshPS);
            deviceContext.PSSetSampler(0, colorSamplerState);
            deviceContext.PSSetConstantBuffer(0, pixelShaderConstantBuffer);
            deviceContext.OMSetRenderTargets(renderTargetView, depthStencilView);
            deviceContext.OMSetDepthStencilState(depthStencilState);

            foreach (var subset in meshDeviceResources.mesh.subsets)
            {
                if (subset.material.textureFilename != null)
                {
                    deviceContext.PSSetShader(meshWithTexturePS);
                    deviceContext.PSSetShaderResource(0, meshDeviceResources.textureRVs[subset]);
                }
                else
                    deviceContext.PSSetShader(meshPS);

                SetPixelShaderConstants(deviceContext, subset.material, pointLight);
                deviceContext.Draw((uint)subset.length, (uint)subset.start);
            }

        }


        ID3D11VertexShader meshVS;
        ID3D11PixelShader meshPS, meshWithTexturePS;
        byte[] shaderByteCode;
        ID3D11DepthStencilState depthStencilState;
        ID3D11RasterizerState rasterizerState;
        ID3D11SamplerState colorSamplerState;
        ID3D11Buffer vertexShaderConstantBuffer, pixelShaderConstantBuffer;
        ID3D11InputLayout vertexInputLayout;
    }
}
