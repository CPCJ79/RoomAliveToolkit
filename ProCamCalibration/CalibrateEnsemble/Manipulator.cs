using Vortice.Direct3D11;
using Vortice.Mathematics;
using System;
using System.Numerics;
using System.Windows.Forms;

namespace RoomAliveToolkit
{
    /// <summary>
    /// Interactively modifies the provided view matrix based on the mouse and keyboard input of the associated control.
    /// Usage: after calling creator with the associate control, set View, Projection and Viewport properties.
    /// Call Update() once per game loop to obtain updated view matrix.
    /// A right handed coordinate system, with +X left, +Y up and +Z forward is assumed.
    /// W, A, S, D, E, and C keys translate in X, Y and Z.
    /// Mouse click and drag rotates around X and Y.
    /// Holding down the ctrl key while dragging rotates around Z.
    /// Holding down the shift key while dragging translates in X and Y.
    /// Mouse wheel translates along Z.
    /// R key resets to a designated 'original view'.
    /// </summary>
    public class Manipulator
    {
        /// <summary>
        /// Create a Manipulator with an associated control.
        /// </summary>
        /// <param name="control"></param>
        public Manipulator(Control control)
        {
            this.control = control;
            control.MouseEnter += panel_MouseEnter;
            control.MouseLeave += panel_MouseLeave;
            control.MouseMove += panel_MouseMove;
            control.MouseHover += panel_MouseHover;
            control.Parent.MouseWheel += Parent_MouseWheel;
            stopwatch.Start();
        }

        /// <summary>
        /// Post-multiply view matrix.
        /// </summary>
        public Matrix4x4 View
        {
            get { return view; }
            set
            {
                // get orientation from view matrix
                orientation = value;
                orientation.Translation = Vector3.Zero;

                // get position from view matrix
                var invR = value;
                invR.Translation = Vector3.Zero;
                invR = Matrix4x4.Transpose(invR);
                position = -(value * invR).Translation;

                UpdateViewMatrix();
            }
        }

        /// <summary>
        /// Post-multiply view matrix which is set when user hits R key.
        /// </summary>
        public Matrix4x4 OriginalView;

        /// <summary>
        /// Post-multiply projection matrix.
        /// </summary>
        public Matrix4x4 Projection;

        /// <summary>
        /// Viewport associated with the control.
        /// </summary>
        public Viewport Viewport;

        /// <summary>
        /// Speed of translation using W, A, S, D, E and C keys. Graphics units/s.
        /// </summary>
        public float TranslationSpeed = 3;

        /// <summary>
        /// Translation speed scale while using Shift key.
        /// </summary>
        public float ShiftTranslationSpeed = 2;

        /// <summary>
        /// Converts mouse wheel units to translation graphics units.
        /// </summary>
        public float MouseWheelStep = 1 / 1000f;

        /// <summary>
        /// Game loop update.
        /// </summary>
        /// <returns>Updated view matrix.</returns>
        public Matrix4x4 Update()
        {
            long now = stopwatch.ElapsedTicks;
            if (mouseOver)
            {
                float dt = (float)(now - lastTime) / (float)System.Diagnostics.Stopwatch.Frequency;
                Update(dt);
            }
            lastTime = now;
            lastMousePosition = mousePosition;
            return view;
        }

        void UpdateViewMatrix()
        {
            view = Matrix4x4.CreateTranslation(-position) * orientation;
        }

        void Translate(Vector3 dir, float step)
        {
            var invR = orientation;
            invR = Matrix4x4.Transpose(invR);
            position += step * Vector3.TransformNormal(dir, invR);
            UpdateViewMatrix();
        }

        // Unproject helper: given screen coordinates, returns a direction vector in world space
        Vector3 Unproject(Vector3 screenPos, Matrix4x4 proj, Matrix4x4 viewMat, Matrix4x4 worldMat)
        {
            // Transform from viewport to NDC
            float x = (screenPos.X - Viewport.X) / Viewport.Width * 2f - 1f;
            float y = 1f - (screenPos.Y - Viewport.Y) / Viewport.Height * 2f;
            float z = screenPos.Z;

            var ndc = new Vector3(x, y, z);

            // Build combined WVP and invert
            var wvp = worldMat * viewMat * proj;
            Matrix4x4.Invert(wvp, out var invWVP);

            var result = Vector3.Transform(ndc, invWVP);
            // Perspective divide handled by Vector3.Transform for Matrix4x4
            return result;
        }

        void Update(float dt)
        {
            // translation
            float step = TranslationSpeed * dt;

            // Z
            if (KeyDown('W'))
                Translate(Vector3.UnitZ, step);
            else if (KeyDown('S'))
                Translate(-Vector3.UnitZ, step);

            // X
            if (KeyDown('A'))
                Translate(Vector3.UnitX, step);
            else if (KeyDown('D'))
                Translate(-Vector3.UnitX, step);

            // Y
            if (KeyDown('E'))
                Translate(Vector3.UnitY, step);
            else if (KeyDown('C'))
                Translate(-Vector3.UnitY, step);

            if (KeyDown('R'))
                View = OriginalView;

            if (mouseWheel != 0)
            {
                Translate(-Vector3.UnitZ, mouseWheel * MouseWheelStep);
                mouseWheel = 0;
            }

            bool mouseDown = (Control.MouseButtons & MouseButtons.Left) != 0;
            bool ctrlDown = KeyDown(0x11);
            bool shiftDown = KeyDown(0x10);

            if (mouseDown && (mousePosition != lastMousePosition))
            {
                if (shiftDown) // translate
                {
                    if (!translating)
                    {
                        translating = true;
                        startPosition = position;
                        startMousePosition = lastMousePosition;
                    }

                    var centerRay = Unproject(new Vector3((float)Viewport.Width / 2f, (float)Viewport.Height / 2f, 0), Projection, orientation, Matrix4x4.Identity);
                    centerRay = Vector3.Normalize(centerRay);

                    var startRay = Unproject(new Vector3(startMousePosition.X, startMousePosition.Y, 0), Projection, orientation, Matrix4x4.Identity);
                    startRay = Vector3.Normalize(startRay);

                    var endRay = Unproject(new Vector3(mousePosition.X, mousePosition.Y, 0), Projection, orientation, Matrix4x4.Identity);
                    endRay = Vector3.Normalize(endRay);

                    float startScale = ShiftTranslationSpeed / Vector3.Dot(centerRay, startRay);
                    float endScale = ShiftTranslationSpeed / Vector3.Dot(centerRay, endRay);

                    var translation = endScale * endRay - startScale * startRay;

                    position = startPosition - translation;
                    UpdateViewMatrix();
                }
                else // rotate
                {
                    if (!rotating)
                    {
                        rotating = true;
                        rotateXY = !ctrlDown;
                        startOrientation = orientation;
                        startMousePosition = lastMousePosition;
                    }

                    Matrix4x4 dR;

                    if (rotateXY)
                    {
                        var startRay = Unproject(new Vector3(startMousePosition.X, startMousePosition.Y, 0), Projection, startOrientation, Matrix4x4.Identity);
                        startRay = Vector3.Normalize(startRay);

                        var endRay = Unproject(new Vector3(mousePosition.X, mousePosition.Y, 0), Projection, startOrientation, Matrix4x4.Identity);
                        endRay = Vector3.Normalize(endRay);

                        float dot = Vector3.Dot(startRay, endRay);
                        dot = Math.Max(-1f, Math.Min(1f, dot));
                        float angle = (float)Math.Acos(dot);
                        var axis = Vector3.Cross(startRay, endRay);
                        axis = Vector3.Normalize(axis);
                        dR = Matrix4x4.CreateFromAxisAngle(axis, angle);
                    }
                    else // rotate around Z
                    {
                        var center = new Vector2((float)control.ClientSize.Width / 2f, (float)control.ClientSize.Height / 2f);

                        var startRay2D = new Vector2(startMousePosition.X, startMousePosition.Y);
                        var endRay = new Vector2(mousePosition.X, mousePosition.Y);
                        startRay2D -= center;
                        endRay -= center;

                        startRay2D = Vector2.Normalize(startRay2D);
                        endRay = Vector2.Normalize(endRay);

                        float angle = (float)Math.Atan2(endRay.Y, endRay.X) - (float)Math.Atan2(startRay2D.Y, startRay2D.X);
                        var axis = Unproject(new Vector3(center.X, center.Y, 0), Projection, startOrientation, Matrix4x4.Identity);
                        axis = Vector3.Normalize(axis);
                        dR = Matrix4x4.CreateFromAxisAngle(axis, angle);
                    }

                    orientation = dR * startOrientation;
                    // Orthonormalize the orientation matrix
                    orientation = Orthonormalize(orientation);

                    UpdateViewMatrix();
                }
            }
            else if (!mouseDown)
            {
                translating = false;
                rotating = false;
            }
        }

        static Matrix4x4 Orthonormalize(Matrix4x4 m)
        {
            // Extract the 3x3 rotation part and re-orthonormalize via Gram-Schmidt
            var x = new Vector3(m.M11, m.M12, m.M13);
            var y = new Vector3(m.M21, m.M22, m.M23);
            var z = new Vector3(m.M31, m.M32, m.M33);

            x = Vector3.Normalize(x);
            y = y - Vector3.Dot(y, x) * x;
            y = Vector3.Normalize(y);
            z = z - Vector3.Dot(z, x) * x - Vector3.Dot(z, y) * y;
            z = Vector3.Normalize(z);

            return new Matrix4x4(
                x.X, x.Y, x.Z, 0,
                y.X, y.Y, y.Z, 0,
                z.X, z.Y, z.Z, 0,
                m.M41, m.M42, m.M43, m.M44);
        }

        void Parent_MouseWheel(object sender, MouseEventArgs e)
        {
            mouseWheel += e.Delta;
        }

        void panel_MouseHover(object sender, EventArgs e)
        {
            mouseOver = true;
        }

        void panel_MouseMove(object sender, MouseEventArgs e)
        {
            mousePosition = e.Location;
        }

        void panel_MouseEnter(object sender, EventArgs e)
        {
            mouseOver = true;
        }

        void panel_MouseLeave(object sender, EventArgs e)
        {
            mouseOver = false;
        }

        bool KeyDown(char key)
        {
            return KeyDown((int)key);
        }

        bool KeyDown(int key)
        {
            return (RoomAliveToolkit.Win32.GetAsyncKeyState(key) >> 15 != 0);
        }

        Matrix4x4 view;
        Vector3 position;
        Matrix4x4 orientation;
        Control control;
        bool rotating = false;
        bool translating = false;
        bool rotateXY;
        Matrix4x4 startOrientation;
        System.Drawing.Point startMousePosition, mousePosition, lastMousePosition;
        Vector3 startPosition;
        bool mouseOver;
        int mouseWheel;
        System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        long lastTime;
    }
}
