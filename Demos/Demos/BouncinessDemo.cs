﻿using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using DemoContentLoader;
using DemoRenderer;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Demos.Demos
{
    /// <summary>
    /// Shows how to configure things to bounce in the absence of a coefficient of restitution.
    /// </summary>
    /// <remarks>
    /// v2 does not support traditional coefficients of restitution because it conflicts with speculative contacts.
    /// All contacts are, however, springs. With a little configuration, you can give objects physically reasonable bounciness.
    /// </remarks>
    public class BouncinessDemo : Demo
    {
        public struct SimpleMaterial
        {
            public SpringSettings SpringSettings;
            public float FrictionCoefficient;
            public float MaximumRecoveryVelocity;
        }
        public unsafe struct BounceCallbacks : INarrowPhaseCallbacks
        {
            public CollidableProperty<SimpleMaterial> CollidableMaterials;

            public void Initialize(Simulation simulation)
            {
                //The callbacks get created before the simulation so that they can be given to the simulation. The property needs a simulation reference, so we hand it over in the initialize.
                CollidableMaterials.Initialize(simulation);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
            {
                //While the engine won't even try creating pairs between statics at all, it will ask about kinematic-kinematic pairs.
                //Those pairs cannot emit constraints since both involved bodies have infinite inertia. Since most of the demos don't need
                //to collect information about kinematic-kinematic pairs, we'll require that at least one of the bodies needs to be dynamic.
                return a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
            {
                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
            {
                //For the purposes of this demo, we'll use multiplicative blending for the friction and choose spring properties according to which collidable has a higher maximum recovery velocity.
                var a = CollidableMaterials[pair.A];
                var b = CollidableMaterials[pair.B];
                pairMaterial.FrictionCoefficient = a.FrictionCoefficient * b.FrictionCoefficient;
                pairMaterial.MaximumRecoveryVelocity = MathF.Max(a.MaximumRecoveryVelocity, b.MaximumRecoveryVelocity);
                pairMaterial.SpringSettings = pairMaterial.MaximumRecoveryVelocity == a.MaximumRecoveryVelocity ? a.SpringSettings : b.SpringSettings;
                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
            {
                return true;
            }

            public void Dispose()
            {
            }
        }

        public unsafe override void Initialize(ContentArchive content, Camera camera)
        {
            camera.Position = new Vector3(0, 40, 200);
            camera.Yaw = 0;
            camera.Pitch = 0;
            //This type of position-based bounciness requires feedback from position error to drive the corrective impulses that make stuff bounce.
            //The briefer a collision is, the more damped the bounce becomes relative to the physical ideal.
            //To counteract this, a substepping timestepper is used. In the demos, we update the simulation at 60hz, so a substep count of 8 means the solver and integrator will run at 480hz.
            //That allows higher stiffnesses to be used since collisions last longer relative to the solver timestep duration.
            //(Note that substepping tends to be an extremely strong simulation stabilizer, so you can usually get away with lower solver iteration counts for better performance. It defaults to 1 velocity iteration per substep.)
            var collidableMaterials = new CollidableProperty<SimpleMaterial>();
            Simulation = Simulation.Create(BufferPool, new BounceCallbacks() { CollidableMaterials = collidableMaterials }, new DemoPoseIntegratorCallbacks(new Vector3(0, -10, 0), 0, 0), 8);

            var shape = new Sphere(1);
            var ballDescription = BodyDescription.CreateDynamic(RigidPose.Identity, shape.ComputeInertia(1), Simulation.Shapes.Add(shape), 1e-2f);

            for (int i = 0; i < 100; ++i)
            {
                for (int j = 0; j < 100; ++j)
                {
                    //We'll drop balls in a grid. From left to right, we increase stiffness, and from back to front (relative to the camera), we'll increase damping.
                    //Note that higher frequency values tend to result in smaller bounces even at 0 damping. This is not physically realistic; it's a byproduct of the solver timestep being too long to properly handle extremely brief contacts.
                    //(Try increasing the substep count above to higher values and watch how the bounce gets closer and closer to equal height across frequency values.)
                    ballDescription.Pose.Position = new Vector3(i * 3 - 99f * 3f / 2f, 100, j * 3 - 230);
                    collidableMaterials.Allocate(Simulation.Bodies.Add(ballDescription)) = new SimpleMaterial { FrictionCoefficient = 1, MaximumRecoveryVelocity = float.MaxValue, SpringSettings = new SpringSettings(5 + 0.25f * i, j * j / 10000f) };
                }
            }

            collidableMaterials.Allocate(Simulation.Statics.Add(new StaticDescription(new Vector3(0, -15f, 0), Simulation.Shapes.Add(new Box(2500, 30, 2500))))) =
                new SimpleMaterial { FrictionCoefficient = 1, MaximumRecoveryVelocity = 2, SpringSettings = new SpringSettings(30, 1) };
        }
    }
}
