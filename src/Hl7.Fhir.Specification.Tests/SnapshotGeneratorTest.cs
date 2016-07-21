﻿/* 
 * Copyright (c) 2014, Furore (info@furore.com) and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://raw.githubusercontent.com/ewoutkramer/fhir-net-api/master/LICENSE
 */

using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Hl7.Fhir.Model;
using Hl7.Fhir.Support;
using System.Diagnostics;
using System.IO;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Serialization;
using System.Collections.Generic;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Specification.Snapshot;
using Hl7.Fhir.Specification.Navigation;

namespace Hl7.Fhir.Specification.Tests
{
	[TestClass]
#if PORTABLE45
	public class PortableSnapshotGeneratorTest
#else
	public class SnapshotGeneratorTest
#endif
	{
		private ArtifactResolver _testSource;
		private readonly SnapshotGeneratorSettings _settings = new SnapshotGeneratorSettings()
		{
			MarkChanges = false,
			ExpandTypeProfiles = true,
			IgnoreMissingTypeProfiles = true
		};

		[TestInitialize]
		public void Setup()
		{
			_testSource = new ArtifactResolver(new CachedArtifactSource(new FileDirectoryArtifactSource("TestData/snapshot-test")));
		}

		[TestMethod]
		//[Ignore]
		public void GenerateSingleSnapshot()
		{
            var sd = _testSource.GetStructureDefinition(@"http://hl7.org/fhir/StructureDefinition/qicore-adverseevent");
            // var sd = _testSource.GetStructureDefinition(@"http://hl7.org/fhir/StructureDefinition/cqif-guidanceresponse");

            Assert.IsNotNull(sd);

			generateSnapshotAndCompare(sd, _testSource);
		}

		// [WMR 20160718] Generate snapshot for extension definition fails with exception:
		// System.ArgumentException: structure is not a constraint or extension

		[TestMethod]
		//[Ignore]
		public void GenerateExtensionSnapshot()
		{
			var sd = _testSource.GetStructureDefinition(@"http://example.org/fhir/StructureDefinition/string-translation");
            Assert.IsNotNull(sd);

			generateSnapshotAndCompare(sd, _testSource);
		}

		// [WMR 20160721] Following profiles are not yet handled (TODO)
		private readonly string[] skippedProfiles =
		{
			// Profiles with invalid type slice (?)
			@"http://hl7.org/fhir/StructureDefinition/uslab-obscode",
			@"http://hl7.org/fhir/StructureDefinition/uslab-obsquantity",
			@"http://hl7.org/fhir/StructureDefinition/uslab-obsother",
			@"http://hl7.org/fhir/StructureDefinition/uslab-obsrange",
			@"http://hl7.org/fhir/StructureDefinition/uslab-obsratio",
			@"http://hl7.org/fhir/StructureDefinition/uslab-obspanel",
			@"http://hl7.org/fhir/StructureDefinition/uslab-patient",
			@"http://hl7.org/fhir/StructureDefinition/uslab-phpatient",
			@"http://hl7.org/fhir/StructureDefinition/sdc-questionnaire",
			@"http://hl7.org/fhir/StructureDefinition/sdc-valueset",
			@"http://hl7.org/fhir/StructureDefinition/daf-medicationstatement",

			// Following profile have inline constraints on extensions
			// => Must resolve & expand the external extension profiles (TODO)
			@"http://hl7.org/fhir/StructureDefinition/qicore-adverseevent",
			@"http://hl7.org/fhir/StructureDefinition/qicore-diagnosticreport",
			@"http://hl7.org/fhir/StructureDefinition/qicore-encounter",
			@"http://hl7.org/fhir/StructureDefinition/qicore-goal",
			@"http://hl7.org/fhir/StructureDefinition/qicore-patient",

			// Inherit custom extension cardinality from CQIF extension definitions (must be resolvable!)
			// @"http://hl7.org/fhir/StructureDefinition/cqif-guidanceresponse",
			// @"http://hl7.org/fhir/StructureDefinition/cqif-evidence",

			// Profiles on profiles
			@"http://hl7.org/fhir/StructureDefinition/cqif-guidanceartifact",    // Derived from cqif-knowledgemodule

            // Differential defines constraint on MedicationOrder.reason[x]
            // Snapshot renames this element to MedicationOrder.reasonCodeableConcept - is this mandatory?
            @"http://hl7.org/fhir/StructureDefinition/gao-medicationorder",
        };

		[TestMethod]
		// [Ignore]
		public void GenerateSnapshot()
		{
			foreach (var original in findConstraintStrucDefs()
				// [WMR 20160721] Skip invalid profiles
				.Where(sd => !skippedProfiles.Contains(sd.Url))
			)
			{
				// nothing to test, original does not have a snapshot
				if (original.Snapshot == null) continue;        

				Debug.WriteLine("Generating Snapshot for " + original.Url);

				generateSnapshotAndCompare(original, _testSource);
			}
		}

		private void generateSnapshotAndCompare(StructureDefinition original, ArtifactResolver source)
		{
			// var generator = new SnapshotGenerator(source, markChanges: false);        
			var generator = new SnapshotGenerator(source, _settings);

			var expanded = (StructureDefinition)original.DeepCopy();
			Assert.IsTrue(original.IsExactly(expanded));

			generator.Generate(expanded);

			var areEqual = original.IsExactly(expanded);

			if (!areEqual)
			{
				var tempPath = Path.GetTempPath();
				File.WriteAllText(Path.Combine(tempPath, "snapshotgen-source.xml"), FhirSerializer.SerializeResourceToXml(original));
				File.WriteAllText(Path.Combine(tempPath, "snapshotgen-dest.xml"), FhirSerializer.SerializeResourceToXml(expanded));
			}

			Assert.IsTrue(areEqual);
		}
   
		private IEnumerable<StructureDefinition> findConstraintStrucDefs()
		{
            var testSDs = _testSource.ListConformanceResources().Where(ci => ci.Type == ResourceType.StructureDefinition);

            foreach (var sdInfo in testSDs)
			{
                // [WMR 20160721] Select all profiles in profiles-others.xml
                var fileName = Path.GetFileNameWithoutExtension(sdInfo.Origin);
                if (fileName == "profiles-others")
                {
                    var sd = _testSource.GetStructureDefinition(sdInfo.Url);

				    if (sd == null) throw new InvalidOperationException(("Source listed canonical url {0} [source {1}], " +
					    "but could not get structure definition by that url later on!").FormatWith(sdInfo.Url, sdInfo.Origin));

                    if (sd.IsConstraint || sd.IsExtension)
					    yield return sd;
                }
            }
        }

		[TestMethod]
		public void MakeDifferentialTree()
		{
			var e = new List<ElementDefinition>();

			e.Add(new ElementDefinition() { Path = "A.B.C1" });
			e.Add(new ElementDefinition() { Path = "A.B.C1" });
			e.Add(new ElementDefinition() { Path = "A.B.C2" });
			e.Add(new ElementDefinition() { Path = "A.B" });
			e.Add(new ElementDefinition() { Path = "A.B.C1.D" });
			e.Add(new ElementDefinition() { Path = "A.D.F" });

			var tree = new DifferentialTreeConstructor(e).MakeTree();
			Assert.IsNotNull(tree);

			var nav = new ElementNavigator(tree);
			Assert.AreEqual(10, nav.Count);

			Assert.IsTrue(nav.MoveToChild("A"));
			Assert.IsTrue(nav.MoveToChild("B"));
			Assert.IsTrue(nav.MoveToChild("C1"));
			Assert.IsTrue(nav.MoveToNext("C1"));
			Assert.IsTrue(nav.MoveToNext("C2"));

			Assert.IsTrue(nav.MoveToParent());  // 1st A.B
			Assert.IsTrue(nav.MoveToNext() && nav.Path == "A.B");  // (now) 2nd A.B
			Assert.IsTrue(nav.MoveToChild("C1"));
			Assert.IsTrue(nav.MoveToChild("D"));

			Assert.IsTrue(nav.MoveToParent());  // A.B.C1
			Assert.IsTrue(nav.MoveToParent());  // A.B (2nd)
			Assert.IsTrue(nav.MoveToNext() && nav.Path == "A.D");
			Assert.IsTrue(nav.MoveToChild("F"));
		}

	 
		[TestMethod]
		public void TestExpandChild()
		{            
			var qStructDef = _testSource.GetStructureDefinition("http://hl7.org/fhir/StructureDefinition/Questionnaire");
			Assert.IsNotNull(qStructDef);
			Assert.IsNotNull(qStructDef.Snapshot);

			var nav = new ElementNavigator(qStructDef.Snapshot.Element);
			
			nav.JumpToFirst("Questionnaire.telecom");
			Assert.IsTrue(SnapshotGenerator.ExpandElement(nav,_testSource, SnapshotGeneratorSettings.Default));
			Assert.IsTrue(nav.MoveToChild("period"), "Did not move into complex datatype ContactPoint");

			nav.JumpToFirst("Questionnaire.group");
			Assert.IsTrue(SnapshotGenerator.ExpandElement(nav,_testSource, SnapshotGeneratorSettings.Default));
			Assert.IsTrue(nav.MoveToChild("title"), "Did not move into internally defined backbone element Group");
		}

	}
}