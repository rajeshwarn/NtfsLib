﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using NTFSLib.Objects.Enums;
using NTFSLib.Provider;

namespace NTFSLib.Objects.Attributes
{
    public class AttributeIndexAllocation : Attribute
    {
        public string Signature { get; set; }
        public ushort OffsetToUSN { get; set; }
        public ushort USNSizeWords { get; set; }
        public ulong LogFileUSN { get; set; }
        public ulong VCNInIndexAllocation { get; set; }
        public uint OffsetToFirstIndex { get; set; }
        public uint SizeOfIndexTotal { get; set; }
        public uint SizeOfIndexAllocated { get; set; }
        public byte HasChildren { get; set; }
        public byte[] USNNumber { get; set; }
        public byte[] USNData { get; set; }

        public IndexEntry[] Entries { get; set; }

        public override AttributeResidentAllow AllowedResidentStates
        {
            get
            {
                return AttributeResidentAllow.NonResident;
            }
        }

        internal override void ParseAttributeNonResidentBody(IMFTProvider provider)
        {
            base.ParseAttributeNonResidentBody(provider);

            byte[] data = new byte[NonResidentHeader.ContentSize];

            // Get all chunks
            foreach (DataFragment fragment in NonResidentHeader.NonResidentFragments)
            {
                byte[] fragmentData = provider.Read(fragment.LCN, (int)fragment.ClusterCount);

                int clusterSize = fragmentData.Length / (int)fragment.ClusterCount;
                int destinationOffset = (int)fragment.StartingVCN * clusterSize;

                Array.Copy(fragmentData, 0, data, destinationOffset,
                           Math.Min(fragmentData.Length, data.Length - destinationOffset));
            }

            // Parse
            Signature = Encoding.ASCII.GetString(data, 0, 4);
            OffsetToUSN = BitConverter.ToUInt16(data, 4);
            USNSizeWords = BitConverter.ToUInt16(data, 6);
            LogFileUSN = BitConverter.ToUInt64(data, 8);
            VCNInIndexAllocation = BitConverter.ToUInt64(data, 16);
            OffsetToFirstIndex = BitConverter.ToUInt32(data, 24);
            SizeOfIndexTotal = BitConverter.ToUInt32(data, 28);
            SizeOfIndexAllocated = BitConverter.ToUInt32(data, 32);
            HasChildren = data[36];

            USNNumber = new byte[2];
            Array.Copy(data, 40, USNNumber, 0, 2);

            USNData = new byte[USNSizeWords * 2];
            Array.Copy(data, OffsetToUSN, USNData, 0, USNSizeWords * 2);

            // TODO: Automatically determine sector size
            // Patch USN Data
            ApplyUSNPatch(data, ((int)SizeOfIndexAllocated + 24) / 512);

            // Parse entries
            List<IndexEntry> entries = new List<IndexEntry>();

            int pointer = (int)(OffsetToFirstIndex + 24);       // Offset is relative to 0x18
            do
            {
                IndexEntry entry = IndexEntry.ParseData(data, (int)SizeOfIndexTotal - pointer + 24, pointer);

                entries.Add(entry);

                if (entry.Flags.HasFlag(MFTIndexEntryFlags.LastEntry))
                    break;

                pointer += entry.Size;
            } while (true);

            Entries = entries.ToArray();
        }

        private void ApplyUSNPatch(byte[] data, int sectors)
        {
            // TODO: Automatically determine sector size
            Debug.Assert(data.Length >= sectors * 512);

            for (int i = 0; i < sectors; i++)
            {
                // Get pointer to the last two bytes
                int blockOffset = i * 512 + 510;

                // Check that they match the USN Number
                Debug.Assert(data[blockOffset] == USNNumber[0]);
                Debug.Assert(data[blockOffset + 1] == USNNumber[1]);

                // Patch in new data
                data[blockOffset] = USNData[i * 2];
                data[blockOffset + 1] = USNData[i * 2 + 1];
            }
        }
    }
}