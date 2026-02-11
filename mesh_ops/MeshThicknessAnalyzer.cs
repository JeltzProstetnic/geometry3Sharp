// Copyright (c) Ryan Schmidt (rms@gradientspace.com) - All Rights Reserved
// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Mesh thickness analyzer for dental CAD and other applications requiring
    /// wall thickness validation. For each vertex, casts a ray along the inverted
    /// vertex normal direction to find the opposing surface, computing the thickness
    /// as the distance to that surface.
    ///
    /// Typical usage:
    ///   var analyzer = new MeshThicknessAnalyzer(mesh);
    ///   analyzer.Compute();
    ///   var thinVertices = analyzer.FindThinVertices(0.5);  // vertices with thickness < 0.5mm
    /// </summary>
    public class MeshThicknessAnalyzer
    {
        public DMesh3 Mesh;
        public DMeshAABBTree3 Spatial;

        /// <summary>
        /// If true, automatically computes mesh normals if not present
        /// </summary>
        public bool AutoComputeNormals = true;

        /// <summary>
        /// Maximum ray distance to search for opposing surface.
        /// Default is double.MaxValue (search entire mesh)
        /// </summary>
        public double MaxDistance = double.MaxValue;

        /// <summary>
        /// Small offset along normal to avoid self-intersection.
        /// Increase if getting zero-thickness results on valid geometry.
        /// </summary>
        public double NormalOffset = MathUtil.ZeroTolerance * 10;

        /// <summary>
        /// Thickness values per vertex. Invalid vertices have double.MaxValue.
        /// Only valid after calling Compute().
        /// </summary>
        public DVector<double> Thicknesses;

        /// <summary>
        /// Set this to be able to cancel running computation
        /// </summary>
        public ProgressCancel Progress = null;

        /// <summary>
        /// if this returns true, abort computation.
        /// </summary>
        protected virtual bool Cancelled()
        {
            return (Progress == null) ? false : Progress.Cancelled();
        }


        public MeshThicknessAnalyzer(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public MeshThicknessAnalyzer(DMesh3 mesh, DMeshAABBTree3 spatial)
        {
            Mesh = mesh;
            Spatial = spatial;
        }


        /// <summary>
        /// Compute thickness for all vertices in the mesh
        /// </summary>
        public virtual bool Compute()
        {
            // Ensure we have normals
            if (!Mesh.HasVertexNormals && AutoComputeNormals) {
                MeshNormals.QuickCompute(Mesh);
            }

            if (!Mesh.HasVertexNormals) {
                throw new Exception("MeshThicknessAnalyzer.Compute: mesh has no vertex normals and AutoComputeNormals is false");
            }

            // Build or use provided spatial data structure
            if (Spatial == null) {
                Spatial = new DMeshAABBTree3(Mesh, true);
            }

            if (Cancelled())
                return false;

            // Initialize thickness array
            int maxVID = Mesh.MaxVertexID;
            Thicknesses = new DVector<double>();
            Thicknesses.resize(maxVID);

            // For each vertex, cast ray along inverted normal (parallelized)
            bool cancel = false;
            gParallel.ForEach(Mesh.VertexIndices(), (vid) => {
                if (cancel) return;
                if (vid % 10 == 0) cancel = Cancelled();
                Thicknesses[vid] = ComputeVertexThickness(vid);
            });

            return !cancel;
        }


        /// <summary>
        /// Compute thickness for a single vertex
        /// </summary>
        protected virtual double ComputeVertexThickness(int vid)
        {
            if (!Mesh.IsVertex(vid))
                return double.MaxValue;

            Vector3d pos = Mesh.GetVertex(vid);
            Vector3f normalF = Mesh.GetVertexNormal(vid);
            Vector3d normal = new Vector3d(normalF.x, normalF.y, normalF.z);
            double len = normal.Normalize();
            if (len < MathUtil.ZeroTolerance)
                return double.MaxValue;  // degenerate normal, skip vertex

            // Cast ray in opposite direction of normal (inward)
            Vector3d rayOrigin = pos + normal * NormalOffset;
            Vector3d rayDir = -normal;

            Ray3d ray = new Ray3d(rayOrigin, rayDir);

            int hitTID = Spatial.FindNearestHitTriangle(ray, MaxDistance);

            if (hitTID == DMesh3.InvalidID) {
                return double.MaxValue;  // No opposing surface found
            }

            // Get actual intersection distance
            IntrRay3Triangle3 intr = MeshQueries.TriangleIntersection(Mesh, hitTID, ray);
            if (intr.Find()) {
                // Distance is the ray parameter, which represents distance along ray
                // Subtract the offset we added
                return Math.Max(0, intr.RayParameter - NormalOffset);
            }

            return double.MaxValue;
        }


        /// <summary>
        /// Get computed thickness for a vertex.
        /// Returns double.MaxValue if vertex is invalid or no opposing surface was found.
        /// Must call Compute() first.
        /// </summary>
        public double GetThickness(int vid)
        {
            if (Thicknesses == null)
                throw new Exception("MeshThicknessAnalyzer.GetThickness: must call Compute() first");
            if (!Mesh.IsVertex(vid))
                return double.MaxValue;
            return Thicknesses[vid];
        }


        /// <summary>
        /// Find all vertices with thickness below the specified threshold
        /// </summary>
        /// <param name="threshold">Maximum thickness (default 0.5mm)</param>
        /// <returns>List of vertex IDs with thickness below threshold</returns>
        public List<int> FindThinVertices(double threshold = 0.5)
        {
            if (Thicknesses == null)
                throw new Exception("MeshThicknessAnalyzer.FindThinVertices: must call Compute() first");

            List<int> thinVerts = new List<int>();

            foreach (int vid in Mesh.VertexIndices()) {
                if (Cancelled())
                    break;

                double thickness = Thicknesses[vid];
                if (thickness < threshold) {
                    thinVerts.Add(vid);
                }
            }

            return thinVerts;
        }


        /// <summary>
        /// Find the minimum thickness in the entire mesh
        /// </summary>
        /// <param name="minVertexID">Output: vertex ID with minimum thickness</param>
        /// <returns>Minimum thickness value</returns>
        public double GetMinimumThickness(out int minVertexID)
        {
            if (Thicknesses == null)
                throw new Exception("MeshThicknessAnalyzer.GetMinimumThickness: must call Compute() first");

            double minThickness = double.MaxValue;
            minVertexID = DMesh3.InvalidID;

            foreach (int vid in Mesh.VertexIndices()) {
                double thickness = Thicknesses[vid];
                if (thickness < minThickness) {
                    minThickness = thickness;
                    minVertexID = vid;
                }
            }

            return minThickness;
        }


        /// <summary>
        /// Get statistics about mesh thickness
        /// </summary>
        public struct ThicknessStats
        {
            public double MinThickness;
            public double MaxThickness;
            public double AverageThickness;
            public int MinVertexID;
            public int MaxVertexID;
            public int ValidVertexCount;
            public int InvalidVertexCount;  // vertices with no opposing surface
        }

        public ThicknessStats ComputeStatistics()
        {
            if (Thicknesses == null)
                throw new Exception("MeshThicknessAnalyzer.ComputeStatistics: must call Compute() first");

            ThicknessStats stats = new ThicknessStats();
            stats.MinThickness = double.MaxValue;
            stats.MaxThickness = 0;
            stats.MinVertexID = DMesh3.InvalidID;
            stats.MaxVertexID = DMesh3.InvalidID;
            stats.ValidVertexCount = 0;
            stats.InvalidVertexCount = 0;

            double sum = 0;

            foreach (int vid in Mesh.VertexIndices()) {
                double thickness = Thicknesses[vid];

                if (thickness >= double.MaxValue) {
                    stats.InvalidVertexCount++;
                    continue;
                }

                stats.ValidVertexCount++;
                sum += thickness;

                if (thickness < stats.MinThickness) {
                    stats.MinThickness = thickness;
                    stats.MinVertexID = vid;
                }

                if (thickness > stats.MaxThickness) {
                    stats.MaxThickness = thickness;
                    stats.MaxVertexID = vid;
                }
            }

            stats.AverageThickness = (stats.ValidVertexCount > 0)
                ? sum / stats.ValidVertexCount
                : 0;

            return stats;
        }
    }
}
