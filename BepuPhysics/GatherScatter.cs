﻿using BepuUtilities;
using BepuUtilities.Memory;
using BepuPhysics.Constraints;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace BepuPhysics
{
    public static class GatherScatter
    {
        //TODO: A lot of stuff in here has grown stale. Other stuff needs to be moved into more appropriate locations. Revisit this in the future once things are baked a little more.

        /// <summary>
        /// Gets a reference to an element from a vector without using pointers, bypassing direct vector access for codegen reasons.
        /// This appears to produce identical assembly to taking the pointer and applying an offset. You can do slightly better for batched accesses
        /// by taking the pointer or reference only once, though the performance difference is small.
        /// This performs no bounds testing!
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ref T Get<T>(ref Vector<T> vector, int index) where T : struct
        {
            return ref Unsafe.Add(ref Unsafe.As<Vector<T>, T>(ref vector), index);

            //For comparison, an implementation like this:
            //return ref *((float*)Unsafe.AsPointer(ref vector) + index);
            //doesn't inline (sometimes?). 
            //The good news is that, in addition to inlining and producing decent assembly, the pointerless approach doesn't open the door
            //for GC related problems and the user doesn't need to pin memory.
        }

        /// <summary>
        /// Copies from one bundle lane to another. The bundle must be a contiguous block of Vector types.
        /// </summary>
        /// <typeparam name="T">Type of the copied bundles.</typeparam>
        /// <param name="sourceBundle">Source bundle of the data to copy.</param>
        /// <param name="sourceInnerIndex">Index of the lane within the source bundle.</param>
        /// <param name="targetBundle">Target bundle of the data to copy.</param>
        /// <param name="targetInnerIndex">Index of the lane within the target bundle.</param>
        /// <remarks>
        /// For performance critical operations, a specialized implementation should be used. This uses a loop with stride equal to a Vector that isn't yet unrolled.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CopyLane<T>(ref T sourceBundle, int sourceInnerIndex, ref T targetBundle, int targetInnerIndex)
        {
            //Note the truncation. Currently used for some types that don't have a size evenly divisible by the Vector<int>.Count * sizeof(int).
            var sizeInInts = (Unsafe.SizeOf<T>() >> 2) & ~BundleIndexing.VectorMask;

            ref var sourceBase = ref Unsafe.Add(ref Unsafe.As<T, int>(ref sourceBundle), sourceInnerIndex);
            ref var targetBase = ref Unsafe.Add(ref Unsafe.As<T, int>(ref targetBundle), targetInnerIndex);

            targetBase = sourceBase;
            //Would be nice if this just auto-unrolled based on the size, considering the jit considers all the relevant bits to be constants!
            //Unfortunately, as of this writing, the jit doesn't.
            //for (int i = Vector<int>.Count; i < sizeInInts; i += Vector<int>.Count)
            //{
            //    Unsafe.Add(ref targetBase, i) = Unsafe.Add(ref sourceBase, i);
            //}

            //To compensate for the compiler, here we go:
            int offset = Vector<int>.Count;
            //8 wide unroll empirically chosen.
            while (offset + Vector<int>.Count * 8 <= sizeInInts)
            {
                Unsafe.Add(ref targetBase, offset) = Unsafe.Add(ref sourceBase, offset); offset += Vector<int>.Count;
                Unsafe.Add(ref targetBase, offset) = Unsafe.Add(ref sourceBase, offset); offset += Vector<int>.Count;
                Unsafe.Add(ref targetBase, offset) = Unsafe.Add(ref sourceBase, offset); offset += Vector<int>.Count;
                Unsafe.Add(ref targetBase, offset) = Unsafe.Add(ref sourceBase, offset); offset += Vector<int>.Count;
                Unsafe.Add(ref targetBase, offset) = Unsafe.Add(ref sourceBase, offset); offset += Vector<int>.Count;
                Unsafe.Add(ref targetBase, offset) = Unsafe.Add(ref sourceBase, offset); offset += Vector<int>.Count;
                Unsafe.Add(ref targetBase, offset) = Unsafe.Add(ref sourceBase, offset); offset += Vector<int>.Count;
                Unsafe.Add(ref targetBase, offset) = Unsafe.Add(ref sourceBase, offset); offset += Vector<int>.Count;
            }
            if (offset + 4 * Vector<int>.Count <= sizeInInts)
            {
                Unsafe.Add(ref targetBase, offset) = Unsafe.Add(ref sourceBase, offset); offset += Vector<int>.Count;
                Unsafe.Add(ref targetBase, offset) = Unsafe.Add(ref sourceBase, offset); offset += Vector<int>.Count;
                Unsafe.Add(ref targetBase, offset) = Unsafe.Add(ref sourceBase, offset); offset += Vector<int>.Count;
                Unsafe.Add(ref targetBase, offset) = Unsafe.Add(ref sourceBase, offset); offset += Vector<int>.Count;
            }
            if (offset + 2 * Vector<int>.Count <= sizeInInts)
            {
                Unsafe.Add(ref targetBase, offset) = Unsafe.Add(ref sourceBase, offset); offset += Vector<int>.Count;
                Unsafe.Add(ref targetBase, offset) = Unsafe.Add(ref sourceBase, offset); offset += Vector<int>.Count;
            }
            if (offset + Vector<int>.Count <= sizeInInts)
            {
                Unsafe.Add(ref targetBase, offset) = Unsafe.Add(ref sourceBase, offset);
            }
        }

        /// <summary>
        /// Gathers values from a quaternion and places them into the first indices of the target wide quaternion.
        /// </summary>
        /// <param name="source">Quaternion to copy values from.</param>
        /// <param name="targetSlot">Wide quaternion to place values into.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GatherSlot(ref BepuUtilities.Quaternion source, ref QuaternionWide targetSlot)
        {
            GetFirst(ref targetSlot.X) = source.X;
            GetFirst(ref targetSlot.Y) = source.Y;
            GetFirst(ref targetSlot.Z) = source.Z;
            GetFirst(ref targetSlot.W) = source.W;
        }

        /// <summary>
        /// Gathers values from a vector and places them into the first indices of the target vector.
        /// </summary>
        /// <param name="source">Vector to copy values from.</param>
        /// <param name="targetSlot">Wide vectorto place values into.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GatherSlot(ref Vector3 source, ref Vector3Wide targetSlot)
        {
            GetFirst(ref targetSlot.X) = source.X;
            GetFirst(ref targetSlot.Y) = source.Y;
            GetFirst(ref targetSlot.Z) = source.Z;
        }

        /// <summary>
        /// Swaps lanes between two bundles. The bundle type must be a contiguous block of Vector types.
        /// </summary>
        /// <typeparam name="T">Type of the swapped bundles.</typeparam>
        /// <param name="bundleA">Source bundle of the data to copy.</param>
        /// <param name="innerIndexA">Index of the lane within the source bundle.</param>
        /// <param name="bundleB">Target bundle of the data to copy.</param>
        /// <param name="innerIndexB">Index of the lane within the target bundle.</param>
        /// <remarks>
        /// For performance critical operations, a specialized implementation should be used. This uses a loop with stride equal to a Vector.
        /// </remarks>
        public static void SwapLanes<T>(ref T bundleA, int innerIndexA, ref T bundleB, int innerIndexB)
        {
            Debug.Assert((Unsafe.SizeOf<T>() & BundleIndexing.VectorMask) == 0,
                "This implementation doesn't truncate the count under the assumption that the type is evenly divisible by the bundle size." +
                "If you later use SwapLanes with a type that breaks this assumption, introduce a truncation as in CopyLanes.");
            var sizeInInts = Unsafe.SizeOf<T>() >> 2;
            ref var aBase = ref Unsafe.Add(ref Unsafe.As<T, int>(ref bundleA), innerIndexA);
            ref var bBase = ref Unsafe.Add(ref Unsafe.As<T, int>(ref bundleB), innerIndexB);
            for (int i = 0; i < sizeInInts; i += Vector<int>.Count)
            {
                var oldA = Unsafe.Add(ref aBase, i);
                ref var b = ref Unsafe.Add(ref bBase, i);
                Unsafe.Add(ref aBase, i) = b;
                b = oldA;
            }
        }

        /// <summary>
        /// Clears a bundle lane using the default value of the specified type. The bundle must be a contiguous block of Vector types, all sharing the same type,
        /// and the first vector must start at the address pointed to by the bundle reference.
        /// </summary>
        /// <typeparam name="TOuter">Type containing one or more Vectors.</typeparam>
        /// <typeparam name="TVector">Type of the vectors to clear.</typeparam>
        /// <param name="bundle">Target bundle to clear a lane in.</param>
        /// <param name="innerIndex">Index of the lane within the target bundle to clear.</param>
        /// <remarks>
        /// For performance critical operations, a specialized implementation should be used. This uses a loop with stride equal to a Vector.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ClearLane<TOuter, TVector>(ref TOuter bundle, int innerIndex) where TVector : struct
        {
            //Note the truncation. This is used on some types that aren't evenly divisible.
            //This should be folded into a single constant by the jit.
            var sizeInElements = (Unsafe.SizeOf<TOuter>() / (Vector<TVector>.Count * Unsafe.SizeOf<TVector>())) * Unsafe.SizeOf<TVector>();
            ref var laneBase = ref Unsafe.Add(ref Unsafe.As<TOuter, TVector>(ref bundle), innerIndex);
            for (int i = 0; i < sizeInElements; i += Vector<int>.Count)
            {
                Unsafe.Add(ref laneBase, i) = default(TVector);
            }
        }
        /// <summary>
        /// Clears a bundle lane using the default value of the specified type. The bundle must be a contiguous block of Vector types, all sharing the same type,
        /// and the first vector must start at the address pointed to by the bundle reference.
        /// </summary>
        /// <typeparam name="TOuter">Type containing one or more Vectors.</typeparam>
        /// <typeparam name="TVector">Type of the vectors to clear.</typeparam>
        /// <param name="bundle">Target bundle to clear a lane in.</param>
        /// <param name="innerIndex">Index of the lane within the target bundle to clear.</param>
        /// <param name="count">Number of elements in the lane to clear.</param>
        /// <remarks>
        /// For performance critical operations, a specialized implementation should be used. This uses a loop with stride equal to a Vector.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ClearLane<TOuter, TVector>(ref TOuter bundle, int innerIndex, int count) where TVector : struct
        {
            ref var laneBase = ref Unsafe.Add(ref Unsafe.As<TOuter, TVector>(ref bundle), innerIndex);
            for (int i = 0; i < count; ++i)
            {
                Unsafe.Add(ref laneBase, i * Vector<TVector>.Count) = default(TVector);
            }
        }


        //AOSOA->AOS
        /// <summary>
        /// Gets a lane of a container of vectors, assuming that the vectors are contiguous.
        /// </summary>
        /// <typeparam name="T">Type of the values to copy out of the container lane.</typeparam>
        /// <param name="startVector">First vector of the contiguous vector region to get a lane within.</param>
        /// <param name="innerIndex">Index of the lane within the vectors.</param>
        /// <param name="values">Reference to a contiguous set of values to hold the values copied out of the vector lane slots.</param>
        /// <param name="valueCount">Number of values to iterate over.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetLane<T>(ref Vector<T> startVector, int innerIndex, ref T values, int valueCount) where T : struct
        {
            ref var lane = ref Get(ref startVector, innerIndex);
            values = lane;
            //Even if the jit recognizes the count as constant, it doesn't unroll anything. Could do it manually, like we did in CopyLane.
            for (int vectorIndex = 1; vectorIndex < valueCount; ++vectorIndex)
            {
                //The multiplication should become a shift; the jit recognizes the count as constant.
                Unsafe.Add(ref values, vectorIndex) = Unsafe.Add(ref lane, vectorIndex * Vector<T>.Count);
            }
        }

        //AOS->AOSOA
        /// <summary>
        /// Sets a lane of a container of vectors, assuming that the vectors are contiguous.
        /// </summary>
        /// <typeparam name="T">Type of the values to copy into the container lane.</typeparam>
        /// <param name="startVector">First vector of the contiguous vector region to set a lane within.</param>
        /// <param name="innerIndex">Index of the lane within the vectors.</param>
        /// <param name="values">Reference to a contiguous set of values to copy into the vector lane slots.</param>
        /// <param name="valueCount">Number of values to iterate over.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetLane<T>(ref Vector<T> startVector, int innerIndex, ref T values, int valueCount) where T : struct
        {
            ref var lane = ref Get(ref startVector, innerIndex);
            lane = values;
            //Even if the jit recognizes the count as constant, it doesn't unroll anything. Could do it manually, like we did in CopyLane.
            for (int vectorIndex = 1; vectorIndex < valueCount; ++vectorIndex)
            {
                //The multiplication should become a shift; the jit recognizes the count as constant.
                Unsafe.Add(ref lane, vectorIndex * Vector<T>.Count) = Unsafe.Add(ref values, vectorIndex);
            }
        }

        /// <summary>
        /// Gets a reference to a shifted bundle container such that the first slot of each bundle covers the given inner index of the original bundle reference.
        /// </summary>
        /// <typeparam name="T">Type of the bundle container.</typeparam>
        /// <param name="bundleContainer">Bundle container whose reference acts as the base for the shifted reference.</param>
        /// <param name="innerIndex">Index within the bundle to access with the shifted reference.</param>
        /// <returns>Shifted bundle container reference covering the inner index of the original bundle reference.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T GetOffsetInstance<T>(ref T bundleContainer, int innerIndex) where T : struct
        {
            return ref Unsafe.As<float, T>(ref Unsafe.Add(ref Unsafe.As<T, float>(ref bundleContainer), innerIndex));
        }

        /// <summary>
        /// Gets a reference to the first element in the vector reference.
        /// </summary>
        /// <typeparam name="T">Type of value held by the vector.</typeparam>
        /// <param name="vector">Vector to pull the first slot value from.</param>
        /// <returns>Reference to the value in the given vector's first slot.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T GetFirst<T>(ref Vector<T> vector) where T : struct
        {
            return ref Unsafe.As<Vector<T>, T>(ref vector);
        }
    }
}

