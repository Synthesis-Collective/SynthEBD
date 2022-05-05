using Mutagen.Bethesda.FormKeys.SkyrimSE;

namespace SynthEBD
{
    public class AssetReplacerHardcodedPaths
    {
        public static HashSet<RecordReplacerSpecifier> ReplacersByPaths = new HashSet<RecordReplacerSpecifier>()
        {
            //humanoid female
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash_01.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash01_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksFemaleHumanoid01LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                            "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash_02.dds\"].TextureSet.Diffuse",
                            "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash02_n.dds\"].TextureSet.NormalOrGloss"
                        },
                        DestFormKeySpecifier = Skyrim.HeadPart.MarksFemaleHumanoid02LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash_03.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash03_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksFemaleHumanoid03LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash_04.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash04_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksFemaleHumanoid04LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash_05.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash05_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksFemaleHumanoid05LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash_06.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash06_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksFemaleHumanoid06LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleRightSideGash_07.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleRightSideGash07_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksFemaleHumanoid07RightGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleRightSideGash_08.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleRightSideGash08_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksFemaleHumanoid08RightGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleRightSideGash_09.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleRightSideGash09_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksFemaleHumanoid09RightGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash_10.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash10_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksFemaleHumanoid10LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash_11.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash11_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksFemaleHumanoid11LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash_12.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\FaceFemaleLeftSideGash12_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksFemaleHumanoid12LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\facefemalerightsidegash_04.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\facefemalerightsidegash04_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestSpecifier = SubgroupCombination.DestinationSpecifier.MarksFemaleHumanoid04RightGashR
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\facefemalerightsidegash_06.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\facefemalerightsidegash06_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestSpecifier = SubgroupCombination.DestinationSpecifier.MarksFemaleHumanoid06RightGashR
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\facefemalerightsidegash_10.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\facefemalerightsidegash10_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksFemaleHumanoid10RightGashR.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\facefemalerightsidegash_11.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\facefemalerightsidegash11_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksFemaleHumanoid11LeftGashR.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\facefemalerightsidegash_12.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Female\\FaceDetails\\facefemalerightsidegash12_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksFemaleHumanoid12LeftGashR.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },

            //humanoid male
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash_01.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash01_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid01LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                            "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash_02.dds\"].TextureSet.Diffuse",
                            "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash02_n.dds\"].TextureSet.NormalOrGloss"
                        },
                        DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid02LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash_03.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash03_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid03LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash_04.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash04_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid04LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash_05.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash05_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid05LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash_06.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash06_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid06LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleRightSideGash_07.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleRightSideGash07_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid07RightGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleRightSideGash_08.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleRightSideGash08_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid08RightGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleRightSideGash_09.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleRightSideGash09_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid09RightGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash_10.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash10_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid10LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash_11.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash11_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid11LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash_12.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\FaceMaleLeftSideGash12_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid12LeftGash.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\facemalerightsidegash_04.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\facemalerightsidegash04_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid04RightGashR.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\facemalerightsidegash_06.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\facemalerightsidegash06_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid06RightGashR.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\facemalerightsidegash_10.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\facemalerightsidegash10_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid10RightGashR.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\facemalerightsidegash_11.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\facemalerightsidegash11_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid11RightGashR.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            },
            new RecordReplacerSpecifier()
            {
                Paths = new HashSet<string>() {
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\facemalerightsidegash_12.dds\"].TextureSet.Diffuse",
                    "HeadParts[TextureSet.Diffuse == \"Actors\\Character\\Male\\FaceDetails\\facemalerightsidegash12_n.dds\"].TextureSet.NormalOrGloss"
                },
                DestFormKeySpecifier = Skyrim.HeadPart.MarksMaleHumanoid12RightGashR.FormKey,
                DestSpecifier = SubgroupCombination.DestinationSpecifier.HeadPartFormKey
            }
        };
    }
}
