using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using RoomAlive.Grpc;

namespace RoomAliveToolkit
{
    public class ProjectorServerService : RoomAlive.Grpc.ProjectorServer.ProjectorServerBase
    {
        Dictionary<int, ProjectorForm> projectorForms = new Dictionary<int, ProjectorForm>();
        ProjectorServerForm projectorServerForm;

        public ProjectorServerService(ProjectorServerForm form)
        {
            this.projectorServerForm = form;
        }

        public override Task<Empty> OpenDisplay(ScreenIndexRequest request, ServerCallContext context)
        {
            int screenIndex = request.ScreenIndex;
            projectorServerForm.Invoke(new Action(() =>
            {
                if (!projectorForms.ContainsKey(screenIndex))
                {
                    var projectorForm = new ProjectorForm(screenIndex);
                    projectorForm.Show();
                    projectorForms[screenIndex] = projectorForm;
                }
            }));
            return Task.FromResult(new Empty());
        }

        public override Task<SizeReply> Size(ScreenIndexRequest request, ServerCallContext context)
        {
            var size = Screen.AllScreens[request.ScreenIndex].Bounds.Size;
            return Task.FromResult(new SizeReply { Width = size.Width, Height = size.Height });
        }

        public override Task<CountReply> ScreenCount(Empty request, ServerCallContext context)
        {
            return Task.FromResult(new CountReply { Count = Screen.AllScreens.Length });
        }

        public override Task<Empty> SetColor(SetColorRequest request, ServerCallContext context)
        {
            projectorServerForm.Invoke(new Action(() =>
            {
                var projectorForm = projectorForms[request.ScreenIndex];
                projectorForm.SetColor(request.R, request.G, request.B);
            }));
            return Task.FromResult(new Empty());
        }

        public override Task<Empty> DisplayName(DisplayNameRequest request, ServerCallContext context)
        {
            projectorServerForm.Invoke(new Action(() =>
            {
                var projectorForm = projectorForms[request.ScreenIndex];
                projectorForm.DisplayName(request.Name);
            }));
            return Task.FromResult(new Empty());
        }

        public override Task<CountReply> NumberOfGrayCodeImages(ScreenIndexRequest request, ServerCallContext context)
        {
            int count = 0;
            projectorServerForm.Invoke(new Action(() =>
            {
                var projectorForm = projectorForms[request.ScreenIndex];
                count = projectorForm.NumberOfGrayCodeImages;
            }));
            return Task.FromResult(new CountReply { Count = count });
        }

        public override Task<Empty> DisplayGrayCode(DisplayGrayCodeRequest request, ServerCallContext context)
        {
            projectorServerForm.Invoke(new Action(() =>
            {
                var projectorForm = projectorForms[request.ScreenIndex];
                projectorForm.DisplayGrayCode(request.Index);
            }));
            return Task.FromResult(new Empty());
        }

        public override Task<Empty> CloseDisplay(ScreenIndexRequest request, ServerCallContext context)
        {
            projectorServerForm.Invoke(new Action(() =>
            {
                int screenIndex = request.ScreenIndex;
                if (projectorForms.ContainsKey(screenIndex))
                {
                    var projectorForm = projectorForms[screenIndex];
                    projectorForm.Close();
                    projectorForms.Remove(screenIndex);
                }
            }));
            return Task.FromResult(new Empty());
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var projectorServerForm = new ProjectorServerForm();
            var service = new ProjectorServerService(projectorServerForm);

            // Start gRPC server on a background thread
            var grpcThread = new Thread(() =>
            {
                var builder = WebApplication.CreateBuilder(args);
                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.ListenAnyIP(9001, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http2;
                    });
                });
                builder.Services.AddGrpc();
                builder.Services.AddSingleton(service);

                var app = builder.Build();
                app.MapGrpcService<ProjectorServerService>();
                app.Run();
            });
            grpcThread.IsBackground = true;
            grpcThread.Start();

            Application.Run(projectorServerForm);
        }
    }
}
