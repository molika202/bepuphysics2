﻿using BepuUtilities;
using DemoRenderer;
using BepuPhysics;
using BepuPhysics.Collidables;
using System.Numerics;
using System;
using DemoContentLoader;
using Demos.Demos;
using DemoUtilities;

namespace Demos.SpecializedTests
{
    /// <summary>
    /// Subjects a bunch of unfortunate ragdolls to a tumble dry cycle.
    /// </summary>
    public class RagdollTubeDemo : Demo
    {
        public unsafe override void Initialize(ContentArchive content, Camera camera)
        {
            camera.Position = new Vector3(0, 9, -40);
            camera.Yaw = MathHelper.Pi;
            camera.Pitch = 0;
            var filters = new CollidableProperty<SubgroupCollisionFilter>();
            Simulation = Simulation.Create(BufferPool, new SubgroupFilteredCallbacks { CollisionFilters = filters }, new DemoPoseIntegratorCallbacks(new Vector3(0, -10, 0)), 4);

            int ragdollIndex = 0;
            var spacing = new Vector3(1.7f, 1.8f, 0.5f);
            int width = 4;
            int height = 4;
            int length = 44;
            var origin = -0.5f * spacing * new Vector3(width - 1, 0, length - 1) + new Vector3(0, 5f, 0);
            for (int i = 0; i < width; ++i)
            {
                for (int j = 0; j < height; ++j)
                {
                    for (int k = 0; k < length; ++k)
                    {
                        RagdollDemo.AddRagdoll(origin + spacing * new Vector3(i, j, k), QuaternionEx.CreateFromAxisAngle(new Vector3(0, 1, 0), MathHelper.Pi * 0.05f), ragdollIndex++, filters, Simulation);
                    }
                }
            }

            var tubeCenter = new Vector3(0, 8, 0);
            const int panelCount = 20;
            const float tubeRadius = 6;
            var panelShape = new Box(MathF.PI * 2 * tubeRadius / panelCount, 1, 80);
            var panelShapeIndex = Simulation.Shapes.Add(panelShape);
            var builder = new CompoundBuilder(BufferPool, Simulation.Shapes, panelCount + 1);
            for (int i = 0; i < panelCount; ++i)
            {
                var rotation = QuaternionEx.CreateFromAxisAngle(Vector3.UnitZ, i * MathHelper.TwoPi / panelCount);
                QuaternionEx.TransformUnitY(rotation, out var localUp);
                var position = localUp * tubeRadius;
                builder.AddForKinematic(panelShapeIndex, (position, rotation), 1);
            }
            builder.AddForKinematic(Simulation.Shapes.Add(new Box(1, 2, panelShape.Length)), new Vector3(0, tubeRadius - 1, 0), 0);
            builder.BuildKinematicCompound(out var children);
            var compound = new BigCompound(children, Simulation.Shapes, BufferPool);
            tubeHandle = Simulation.Bodies.Add(BodyDescription.CreateKinematic(tubeCenter, (default, new Vector3(0, 0, .25f)), Simulation.Shapes.Add(compound), 0f));
            filters[tubeHandle] = new SubgroupCollisionFilter(int.MaxValue);
            builder.Dispose();

            var staticShape = new Box(300, 1, 300);
            var staticShapeIndex = Simulation.Shapes.Add(staticShape);
            var staticDescription = new StaticDescription(new Vector3(0, -0.5f, 0), staticShapeIndex);
            Simulation.Statics.Add(staticDescription);
        }

        BodyHandle tubeHandle;

        //public override void Update(Window window, Camera camera, Input input, float dt)
        //{
        //    base.Update(window, camera, input, dt);

        //    Console.WriteLine($"Constraint count: {Simulation.Solver.CountConstraints()}");

        //    Console.WriteLine($"Constraints affecting tube: {Simulation.Bodies.GetBodyReference(tubeHandle).Constraints.Count}");
        //}

    }
}


