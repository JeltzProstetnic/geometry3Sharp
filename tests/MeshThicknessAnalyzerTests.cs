using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;

namespace g3.tests
{
    /// <summary>
    /// Tests for gs.MeshThicknessAnalyzer.
    /// Standalone console runner — no test framework dependency.
    ///
    /// Run with: dotnet run --project tests/MeshThicknessAnalyzerTests.csproj
    /// </summary>
    public class MeshThicknessAnalyzerTests
    {
        private static int testsPassed = 0;
        private static int testsFailed = 0;
        private static int testsTotal = 0;

        public static void Main(string[] args)
        {
            Console.WriteLine("==========================================");
            Console.WriteLine("MeshThicknessAnalyzer Test Suite");
            Console.WriteLine("==========================================\n");

            TestUniformSphereMesh();
            TestBoxMeshWithKnownThickness();
            TestThinRegionDetection();
            TestThresholdConfiguration();
            TestEmptyMesh();
            TestSingleTriangleMesh();
            TestDegenerateMesh();
            TestOpenMeshWithBoundaries();
            TestNormalOrientationInward();
            TestNormalOrientationOutward();
            TestLargeThicknessValues();
            TestVeryThinRegions();
            TestMeshWithNoHits();
            TestPerformanceWithLargeMesh();
            TestComputeStatistics();
            TestGetMinimumThickness();
            TestComputeRequiredBeforeQuery();

            Console.WriteLine("\n==========================================");
            Console.WriteLine("Test Results Summary");
            Console.WriteLine("==========================================");
            Console.WriteLine($"Total Tests:  {testsTotal}");
            Console.WriteLine($"Passed:       {testsPassed}");
            Console.WriteLine($"Failed:       {testsFailed}");
            Console.WriteLine($"Success Rate: {(testsTotal > 0 ? (testsPassed * 100.0 / testsTotal) : 0):F1}%");
            Console.WriteLine("==========================================\n");

            Environment.Exit(testsFailed > 0 ? 1 : 0);
        }

        #region Test Cases

        /// <summary>
        /// Uniform sphere: thickness should be approximately 2*radius (diameter).
        /// </summary>
        private static void TestUniformSphereMesh()
        {
            string testName = "TestUniformSphereMesh";
            try
            {
                double radius = 10.0;
                DMesh3 mesh = MakeSphere(radius, 8);

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                bool success = analyzer.Compute();
                AssertTrue(success, "Compute() should return true", testName);

                double expectedThickness = 2.0 * radius;
                double tolerance = radius * 0.3; // 30% for sphere discretization

                int validCount = 0;
                foreach (int vid in mesh.VertexIndices())
                {
                    double t = analyzer.GetThickness(vid);
                    if (t < double.MaxValue && Math.Abs(t - expectedThickness) < tolerance)
                        validCount++;
                }

                double validPct = (validCount * 100.0) / mesh.VertexCount;
                AssertTrue(validPct > 70,
                    $"At least 70% should have thickness near {expectedThickness:F2} (got {validPct:F1}%)",
                    testName);

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        /// <summary>
        /// Box mesh: vertices on opposite faces should measure the box width.
        /// </summary>
        private static void TestBoxMeshWithKnownThickness()
        {
            string testName = "TestBoxMeshWithKnownThickness";
            try
            {
                double sideLength = 20.0;
                var boxGen = new TrivialBox3Generator
                {
                    Box = new Box3d(Vector3d.Zero,
                        new Vector3d(sideLength / 2, sideLength / 2, sideLength / 2)),
                    NoSharedVertices = true
                };
                boxGen.Generate();
                DMesh3 mesh = boxGen.MakeDMesh();

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                bool success = analyzer.Compute();
                AssertTrue(success, "Compute() should return true", testName);

                int validCount = 0;
                double tolerance = sideLength * 0.2;
                foreach (int vid in mesh.VertexIndices())
                {
                    double t = analyzer.GetThickness(vid);
                    if (t < double.MaxValue && Math.Abs(t - sideLength) < tolerance)
                        validCount++;
                }

                AssertTrue(validCount > 0,
                    "At least some vertices should have valid thickness near box width", testName);

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        /// <summary>
        /// Small sphere (diameter 0.3mm) — all vertices should be flagged thin at 0.5 threshold.
        /// Uses a sphere to avoid degenerate edge hits from aligned parallel planes.
        /// </summary>
        private static void TestThinRegionDetection()
        {
            string testName = "TestThinRegionDetection";
            try
            {
                double targetThickness = 0.3;
                double radius = targetThickness / 2.0;
                DMesh3 mesh = MakeSphere(radius, 6);

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                bool success = analyzer.Compute();
                AssertTrue(success, "Compute() should return true", testName);

                List<int> thinVerts = analyzer.FindThinVertices(0.5);
                AssertTrue(thinVerts.Count > 0, "Should detect thin vertices", testName);

                // Nearly all vertices should be thin (diameter < 0.5)
                double thinPct = (thinVerts.Count * 100.0) / mesh.VertexCount;
                AssertTrue(thinPct > 80,
                    $"At least 80% should be thin (got {thinPct:F1}%)", testName);

                // Check measured thickness is near the diameter
                var measured = new List<double>();
                foreach (int vid in mesh.VertexIndices())
                {
                    double t = analyzer.GetThickness(vid);
                    if (t < double.MaxValue)
                        measured.Add(t);
                }
                if (measured.Count > 0)
                {
                    double avg = measured.Average();
                    AssertTrue(Math.Abs(avg - targetThickness) < targetThickness * 0.5,
                        $"Average thickness should be near {targetThickness} (got {avg:F3})", testName);
                }

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        /// <summary>
        /// High threshold flags more vertices as thin than low threshold.
        /// Uses the same analyzer — just calls FindThinVertices with different values.
        /// </summary>
        private static void TestThresholdConfiguration()
        {
            string testName = "TestThresholdConfiguration";
            try
            {
                DMesh3 mesh = MakeSphere(5.0, 6);
                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);
                analyzer.Compute();

                List<int> thinHigh = analyzer.FindThinVertices(100.0);
                List<int> thinLow = analyzer.FindThinVertices(0.01);

                AssertTrue(thinHigh.Count > thinLow.Count,
                    $"High threshold ({thinHigh.Count}) should flag more than low ({thinLow.Count})",
                    testName);

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        /// <summary>
        /// Empty mesh: should complete without error.
        /// </summary>
        private static void TestEmptyMesh()
        {
            string testName = "TestEmptyMesh";
            try
            {
                DMesh3 mesh = new DMesh3();
                mesh.EnableVertexNormals(Vector3f.AxisY);
                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                bool success = analyzer.Compute();
                AssertTrue(success, "Compute() should succeed for empty mesh", testName);

                List<int> thinVerts = analyzer.FindThinVertices(0.5);
                AssertTrue(thinVerts.Count == 0,
                    "Empty mesh should have zero thin vertices", testName);

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        /// <summary>
        /// Single triangle: minimal valid mesh, no opposing surface expected.
        /// </summary>
        private static void TestSingleTriangleMesh()
        {
            string testName = "TestSingleTriangleMesh";
            try
            {
                DMesh3 mesh = new DMesh3();
                mesh.EnableVertexNormals(Vector3f.AxisY);

                mesh.AppendVertex(new NewVertexInfo {
                    v = new Vector3d(0, 0, 0), n = Vector3f.AxisY });
                mesh.AppendVertex(new NewVertexInfo {
                    v = new Vector3d(1, 0, 0), n = Vector3f.AxisY });
                mesh.AppendVertex(new NewVertexInfo {
                    v = new Vector3d(0, 0, 1), n = Vector3f.AxisY });
                mesh.AppendTriangle(0, 1, 2);

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                bool success = analyzer.Compute();
                AssertTrue(success, "Compute() should return true", testName);

                // Single open triangle — no meaningful thickness measurement
                // Just verify it didn't crash and all vertices were processed
                foreach (int vid in mesh.VertexIndices())
                    analyzer.GetThickness(vid); // should not throw

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        /// <summary>
        /// Degenerate mesh (two vertices at same position): should not crash.
        /// </summary>
        private static void TestDegenerateMesh()
        {
            string testName = "TestDegenerateMesh";
            try
            {
                DMesh3 mesh = new DMesh3();
                mesh.EnableVertexNormals(Vector3f.AxisY);

                mesh.AppendVertex(new NewVertexInfo {
                    v = new Vector3d(0, 0, 0), n = Vector3f.AxisY });
                mesh.AppendVertex(new NewVertexInfo {
                    v = new Vector3d(0, 0, 0), n = Vector3f.AxisY }); // coincident
                mesh.AppendVertex(new NewVertexInfo {
                    v = new Vector3d(1, 0, 0), n = Vector3f.AxisY });
                mesh.AppendTriangle(0, 1, 2);

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                bool success = analyzer.Compute();
                AssertTrue(success, "Should handle degenerate mesh without crashing", testName);

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        /// <summary>
        /// Open mesh with boundary edges: should process all vertices.
        /// </summary>
        private static void TestOpenMeshWithBoundaries()
        {
            string testName = "TestOpenMeshWithBoundaries";
            try
            {
                DMesh3 mesh = new DMesh3();
                mesh.EnableVertexNormals(Vector3f.AxisY);

                mesh.AppendVertex(new NewVertexInfo {
                    v = new Vector3d(0, 0, 0), n = Vector3f.AxisY });
                mesh.AppendVertex(new NewVertexInfo {
                    v = new Vector3d(5, 0, 0), n = Vector3f.AxisY });
                mesh.AppendVertex(new NewVertexInfo {
                    v = new Vector3d(5, 0, 5), n = Vector3f.AxisY });
                mesh.AppendVertex(new NewVertexInfo {
                    v = new Vector3d(0, 0, 5), n = Vector3f.AxisY });
                mesh.AppendTriangle(0, 1, 2);
                mesh.AppendTriangle(0, 2, 3);

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                bool success = analyzer.Compute();
                AssertTrue(success, "Compute() should return true", testName);

                int processedCount = 0;
                foreach (int vid in mesh.VertexIndices())
                {
                    analyzer.GetThickness(vid); // should not throw
                    processedCount++;
                }
                AssertTrue(processedCount == mesh.VertexCount,
                    "Should process all vertices in open mesh", testName);

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        /// <summary>
        /// Sphere with flipped (inward) normals: rays go outward, mostly missing.
        /// Should complete without error.
        /// </summary>
        private static void TestNormalOrientationInward()
        {
            string testName = "TestNormalOrientationInward";
            try
            {
                double radius = 8.0;
                DMesh3 mesh = MakeSphere(radius, 6);

                foreach (int vid in mesh.VertexIndices())
                    mesh.SetVertexNormal(vid, -mesh.GetVertexNormal(vid));

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                bool success = analyzer.Compute();
                AssertTrue(success, "Compute() should succeed with inward normals", testName);

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        /// <summary>
        /// Sphere with standard outward normals: rays go inward, should hit opposite side.
        /// </summary>
        private static void TestNormalOrientationOutward()
        {
            string testName = "TestNormalOrientationOutward";
            try
            {
                double radius = 8.0;
                DMesh3 mesh = MakeSphere(radius, 6);

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                bool success = analyzer.Compute();
                AssertTrue(success, "Compute() should succeed", testName);

                int hitCount = 0;
                foreach (int vid in mesh.VertexIndices())
                {
                    if (analyzer.GetThickness(vid) < double.MaxValue)
                        hitCount++;
                }
                AssertTrue(hitCount > mesh.VertexCount / 2,
                    $"Most vertices should have valid thickness ({hitCount}/{mesh.VertexCount})",
                    testName);

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        /// <summary>
        /// Large sphere (radius=1000): no NaN or Infinity in results.
        /// </summary>
        private static void TestLargeThicknessValues()
        {
            string testName = "TestLargeThicknessValues";
            try
            {
                DMesh3 mesh = MakeSphere(1000.0, 5);

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                bool success = analyzer.Compute();
                AssertTrue(success, "Compute() should succeed", testName);

                foreach (int vid in mesh.VertexIndices())
                {
                    double t = analyzer.GetThickness(vid);
                    AssertTrue(!double.IsNaN(t) && !double.IsInfinity(t),
                        $"Vertex {vid}: thickness should not be NaN or Infinity", testName);
                }

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        /// <summary>
        /// Very small sphere (diameter 0.01mm): all vertices flagged thin.
        /// </summary>
        private static void TestVeryThinRegions()
        {
            string testName = "TestVeryThinRegions";
            try
            {
                double targetThickness = 0.01;
                DMesh3 mesh = MakeSphere(targetThickness / 2.0, 6);

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                bool success = analyzer.Compute();
                AssertTrue(success, "Compute() should succeed", testName);

                List<int> thinVerts = analyzer.FindThinVertices(0.5);
                double thinPct = (thinVerts.Count * 100.0) / mesh.VertexCount;
                AssertTrue(thinPct > 80,
                    $"At least 80% should be thin (got {thinPct:F1}%)", testName);

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        /// <summary>
        /// Single flat plane: rays may self-intersect or miss entirely.
        /// Either outcome is acceptable — just verify no crash.
        /// </summary>
        private static void TestMeshWithNoHits()
        {
            string testName = "TestMeshWithNoHits";
            try
            {
                DMesh3 mesh = new DMesh3();
                mesh.EnableVertexNormals(Vector3f.AxisY);

                mesh.AppendVertex(new NewVertexInfo {
                    v = new Vector3d(-2, 0, -2), n = Vector3f.AxisY });
                mesh.AppendVertex(new NewVertexInfo {
                    v = new Vector3d(2, 0, -2), n = Vector3f.AxisY });
                mesh.AppendVertex(new NewVertexInfo {
                    v = new Vector3d(2, 0, 2), n = Vector3f.AxisY });
                mesh.AppendVertex(new NewVertexInfo {
                    v = new Vector3d(-2, 0, 2), n = Vector3f.AxisY });
                mesh.AppendTriangle(0, 1, 2);
                mesh.AppendTriangle(0, 2, 3);

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                bool success = analyzer.Compute();
                AssertTrue(success, "Compute() should handle single-sided plane", testName);

                // Verify no NaN or Infinity
                foreach (int vid in mesh.VertexIndices())
                {
                    double t = analyzer.GetThickness(vid);
                    AssertTrue(!double.IsNaN(t) && !double.IsInfinity(t),
                        $"Vertex {vid}: should not be NaN or Infinity", testName);
                }

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        /// <summary>
        /// Performance: dense sphere should complete in under 30 seconds.
        /// </summary>
        private static void TestPerformanceWithLargeMesh()
        {
            string testName = "TestPerformanceWithLargeMesh";
            try
            {
                DMesh3 mesh = MakeSphere(15.0, 20);
                Console.WriteLine($"  ({mesh.VertexCount} vertices, {mesh.TriangleCount} triangles)");

                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);

                var start = DateTime.Now;
                bool success = analyzer.Compute();
                var elapsed = DateTime.Now - start;

                AssertTrue(success, "Compute() should succeed", testName);
                Console.WriteLine($"  Computation time: {elapsed.TotalMilliseconds:F0}ms");

                AssertTrue(elapsed.TotalSeconds < 30,
                    $"Should complete in < 30s (took {elapsed.TotalSeconds:F1}s)", testName);

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        /// <summary>
        /// ComputeStatistics: verify ThicknessStats fields are populated on a sphere.
        /// </summary>
        private static void TestComputeStatistics()
        {
            string testName = "TestComputeStatistics";
            try
            {
                DMesh3 mesh = MakeSphere(5.0, 6);
                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);
                analyzer.Compute();

                var stats = analyzer.ComputeStatistics();

                AssertTrue(stats.ValidVertexCount > 0,
                    "Should have valid vertices", testName);
                AssertTrue(stats.MinThickness > 0,
                    "MinThickness should be > 0", testName);
                AssertTrue(stats.MaxThickness >= stats.MinThickness,
                    "MaxThickness >= MinThickness", testName);
                AssertTrue(stats.AverageThickness > 0,
                    "AverageThickness should be > 0", testName);
                AssertTrue(stats.MinVertexID != DMesh3.InvalidID,
                    "MinVertexID should be valid", testName);
                AssertTrue(stats.MaxVertexID != DMesh3.InvalidID,
                    "MaxVertexID should be valid", testName);

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        /// <summary>
        /// GetMinimumThickness: should match the vertex's individual thickness.
        /// </summary>
        private static void TestGetMinimumThickness()
        {
            string testName = "TestGetMinimumThickness";
            try
            {
                DMesh3 mesh = MakeSphere(5.0, 6);
                var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
                var analyzer = new MeshThicknessAnalyzer(mesh, spatial);
                analyzer.Compute();

                int minVid;
                double minThickness = analyzer.GetMinimumThickness(out minVid);

                AssertTrue(minThickness > 0,
                    "Min thickness should be > 0 for sphere", testName);
                AssertTrue(minVid != DMesh3.InvalidID,
                    "Should identify the min vertex", testName);

                double direct = analyzer.GetThickness(minVid);
                AssertTrue(Math.Abs(minThickness - direct) < 1e-10,
                    "GetMinimumThickness should match GetThickness for that vertex", testName);

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        /// <summary>
        /// Calling query methods before Compute() should throw.
        /// </summary>
        private static void TestComputeRequiredBeforeQuery()
        {
            string testName = "TestComputeRequiredBeforeQuery";
            try
            {
                DMesh3 mesh = MakeSphere(5.0, 4);
                var analyzer = new MeshThicknessAnalyzer(mesh);

                AssertThrows(() => analyzer.GetThickness(0),
                    "GetThickness before Compute()", testName);
                AssertThrows(() => analyzer.FindThinVertices(0.5),
                    "FindThinVertices before Compute()", testName);
                AssertThrows(() => analyzer.ComputeStatistics(),
                    "ComputeStatistics before Compute()", testName);
                AssertThrows(() => { int v; analyzer.GetMinimumThickness(out v); },
                    "GetMinimumThickness before Compute()", testName);

                PassTest(testName);
            }
            catch (Exception ex) { FailTest(testName, ex.Message); }
        }

        #endregion

        #region Helpers

        private static DMesh3 MakeSphere(double radius, int edgeVertices)
        {
            var gen = new Sphere3Generator_NormalizedCube
            {
                EdgeVertices = edgeVertices,
                Radius = radius,
                Box = new Box3d(Vector3d.Zero, new Vector3d(radius, radius, radius))
            };
            gen.Generate();
            return gen.MakeDMesh();
        }

        private static void AssertTrue(bool condition, string message, string testName)
        {
            if (!condition)
                throw new Exception($"Assertion failed: {message}");
        }

        private static void AssertThrows(Action action, string description, string testName)
        {
            bool threw = false;
            try { action(); }
            catch (Exception) { threw = true; }
            if (!threw)
                throw new Exception($"Expected exception from {description}");
        }

        private static void PassTest(string testName)
        {
            testsPassed++;
            testsTotal++;
            Console.WriteLine($"  PASS: {testName}");
        }

        private static void FailTest(string testName, string error)
        {
            testsFailed++;
            testsTotal++;
            Console.WriteLine($"  FAIL: {testName}");
            Console.WriteLine($"    Error: {error}");
        }

        #endregion
    }
}
