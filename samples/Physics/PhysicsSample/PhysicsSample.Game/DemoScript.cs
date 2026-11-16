// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections.Generic;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Constraints;
using Stride.BepuPhysics.Definitions.Contacts;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;
using System.Threading.Tasks;

namespace PhysicsSample12
{
    /// <summary>
    /// Joins a cube and a sphere with Bepu constraints, one demo after the other. A Bepu joint is made of constraints
    /// that each remove some freedom: a slider is a point on a line with a limit along it, a cone twist a ball socket
    /// with swing and twist limits.
    /// </summary>
    public class DemoScript : StartupScript, IContactHandler
    {
        private BepuSimulation simulation;

        public Entity cube;
        public Entity sphere;

        public SpriteFont Font;

        private readonly List<ConstraintComponentBase> currentConstraints = new List<ConstraintComponentBase>();
        private readonly List<Action> PhysicsSampleList = new List<Action>();
        private int constraintIndex;
        private bool isSlider;

        private BodyComponent cubeBody;
        private BodyComponent sphereBody;

        private TextBlock constraintNameBlock;

        public bool NoContactResponse => false;

        public override void Start()
        {
            simulation = Entity.GetSimulation();
            simulation.PoseGravity = new Vector3(0, -9, 0);

            // A negative threshold keeps a body awake
            cubeBody = cube.Get<BodyComponent>();
            cubeBody.SleepThreshold = -1;
            sphereBody = sphere.Get<BodyComponent>();
            sphereBody.SleepThreshold = -1;

            // Create the UI
            constraintNameBlock = new TextBlock
            {
                Font = Font,
                TextSize = 55,
                TextColor = Color.White,
            };
            constraintNameBlock.SetCanvasPinOrigin(new Vector3(0.5f, 0.5f, 0));
            constraintNameBlock.SetCanvasRelativePosition(new Vector3(0.5f, 0.83f, 0));

            Entity.Get<UIComponent>().Page = new UIPage
            {
                RootElement = new Canvas
                {
                    Children =
                    {
                        constraintNameBlock,
                        CreateButton("Next", Font, 1),
                        CreateButton("Previous", Font, -1)
                    }
                }
            };

            // Create and initialize constraint
            PhysicsSampleList.Add(CreateBallSocketConstraint);
            PhysicsSampleList.Add(CreateHingeConstraint);
            PhysicsSampleList.Add(CreateGearConstraint);
            PhysicsSampleList.Add(CreateSliderConstraint);
            PhysicsSampleList.Add(CreateConeTwistConstraint);
            PhysicsSampleList.Add(CreateWeldConstraint);

            RemoveConstraint();
            PhysicsSampleList[constraintIndex]();

            // For the slider: the cube tells when the sphere hits it, to fire the sphere again
            cubeBody.ContactEventHandler = this;
        }

        void IContactHandler.OnStartedTouching<TManifold>(Contacts<TManifold> contacts)
        {
            if (!isSlider || contacts.Other != sphereBody) return;
            sphereBody.LinearVelocity = Vector3.Zero; //clear any existing velocity
            sphereBody.ApplyLinearImpulse(new Vector3(-25, 0, 0)); //fire impulse
        }

        void CreateBallSocketConstraint()
        {
            // The point of the sphere 4 units to its right stays at the center of the cube
            AddConstraint(new BallSocketConstraintComponent { LocalOffsetA = Vector3.Zero, LocalOffsetB = new Vector3(4, 0, 0) });
            constraintNameBlock.Text = "Ball Socket";

            //there are no limits so the sphere will orbit once we apply this
            sphereBody.ApplyLinearImpulse(new Vector3(0, 0, 18));
        }

        void CreateHingeConstraint()
        {
            AddConstraint(new HingeConstraintComponent
            {
                LocalOffsetA = Vector3.Zero,
                LocalHingeAxisA = Vector3.UnitZ,
                LocalOffsetB = new Vector3(4, 0, 0),
                LocalHingeAxisB = Vector3.UnitZ,
            });
            constraintNameBlock.Text = "Hinge";

            //this impulse is along the axis of the hinge, which stops it
            sphereBody.ApplyLinearImpulse(new Vector3(0, 0, 18));
        }

        void CreateGearConstraint()
        {
            // Both turn in place: no gravity, and nothing else moves them
            cubeBody.Kinematic = false;
            cubeBody.Gravity = false;
            sphereBody.Gravity = false;

            // The cube turns around the X axis at half the speed of the sphere
            AddConstraint(new AngularAxisGearMotorConstraintComponent { LocalAxisA = Vector3.UnitX, VelocityScale = 0.5f }, sphereBody, cubeBody);
            constraintNameBlock.Text = "Angular Axis Gear Motor";

            //this velocity will start a motion in the sphere which gets propagated into the cube
            sphereBody.AngularVelocity = new Vector3(25, 0, 0);
        }

        void CreateSliderConstraint()
        {
            // The sphere stays on the line of the X axis of the cube, at most 4 units to its left
            AddConstraint(new PointOnLineServoConstraintComponent { LocalOffsetA = Vector3.Zero, LocalOffsetB = Vector3.Zero, LocalDirection = Vector3.UnitX });
            AddConstraint(new LinearAxisLimitConstraintComponent { LocalOffsetA = Vector3.Zero, LocalOffsetB = Vector3.Zero, LocalAxis = Vector3.UnitX, MinimumOffset = -4, MaximumOffset = 0 });
            constraintNameBlock.Text = "Slider";
            isSlider = true;

            //applying this impulse will let the sphere reach the lower linear limit, from which it springs back towards the cube
            sphereBody.ApplyLinearImpulse(new Vector3(-25, 0, 0));
        }

        void CreateConeTwistConstraint()
        {
            // A ball socket, and limits on how far the X axis of the sphere swings and twists from that of the cube
            AddConstraint(new BallSocketConstraintComponent { LocalOffsetA = Vector3.Zero, LocalOffsetB = new Vector3(4, 0, 0) });
            AddConstraint(new SwingLimitConstraintComponent { AxisLocalA = Vector3.UnitX, AxisLocalB = Vector3.UnitX, MaximumSwingAngle = 0.5f });
            // A twist turns around the Z axis of the bases: these turn it to X
            var twistBasis = Quaternion.RotationY(MathUtil.PiOverTwo);
            AddConstraint(new TwistLimitConstraintComponent { LocalBasisA = twistBasis, LocalBasisB = twistBasis, MinimumAngle = -0.5f, MaximumAngle = 0.5f });
            constraintNameBlock.Text = "Cone Twist";

            //applying this impulse will show the cone limits
            sphereBody.ApplyLinearImpulse(new Vector3(0, 0, 18));
        }

        void CreateWeldConstraint()
        {
            // The sphere keeps its place and orientation 4 units to the left of the cube
            AddConstraint(new WeldConstraintComponent { LocalOffset = new Vector3(-4, 0, 0), LocalOrientation = Quaternion.Identity });
            constraintNameBlock.Text = "Weld";

            //the weld's spring gives a little under this impulse, and brings the sphere back
            sphereBody.ApplyLinearImpulse(new Vector3(0, 0, 18));
        }

        // A constraint is in the simulation while it is on an entity
        private void AddConstraint<T>(T constraint, BodyComponent a = null, BodyComponent b = null) where T : ConstraintComponentBase, ITwoBody
        {
            constraint.A = a ?? cubeBody;
            constraint.B = b ?? sphereBody;
            sphere.Add(constraint);
            currentConstraints.Add(constraint);
        }

        private void RemoveConstraint()
        {
            //Remove the current constraints
            foreach (var constraint in currentConstraints)
                sphere.Remove(constraint);
            currentConstraints.Clear();
            isSlider = false;

            //Stop motion and reset the bodies: the cube does not move, the sphere falls
            cubeBody.Kinematic = true;
            cubeBody.Gravity = true;
            sphereBody.Gravity = true;

            cubeBody.Teleport(new Vector3(2, 0, -9), Quaternion.Identity);
            cubeBody.AngularVelocity = Vector3.Zero;
            cubeBody.LinearVelocity = Vector3.Zero;

            sphereBody.Teleport(new Vector3(-2, 0, -9), Quaternion.Identity);
            sphereBody.AngularVelocity = Vector3.Zero;
            sphereBody.LinearVelocity = Vector3.Zero;
        }

        private void ChangeConstraint(int offset)
        {
            RemoveConstraint();

            // calculate constraint index
            constraintIndex = (constraintIndex + offset + PhysicsSampleList.Count) % PhysicsSampleList.Count;

            PhysicsSampleList[constraintIndex]();
        }

        private Button CreateButton(string text, SpriteFont font, int offset)
        {
            var button = new Button
            {
                Name = text,
                Padding = Thickness.UniformRectangle(15),
                HorizontalAlignment = HorizontalAlignment.Left,
                Content = new TextBlock { Text = text, Font = font, TextSize = 35, TextColor = new Color(200, 200, 200, 255) },
                BackgroundColor = new Color(new Vector4(0.2f, 0.2f, 0.2f, 0.2f)),
            };

            button.Click += (sender, args) =>
            {
                Script.AddTask(() =>
                {
                    ChangeConstraint(offset);
                    return Task.CompletedTask;
                });
            };

            button.SetCanvasPinOrigin(new Vector3(offset > 0 ? 1 : 0, 0.5f, 0));
            button.SetCanvasRelativePosition(new Vector3(offset > 0 ? 0.87f : 0.13f, 0.93f, 0));

            return button;
        }

        public override void Cancel()
        {
            RemoveConstraint();
        }
    }
}
