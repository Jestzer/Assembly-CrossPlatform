using Blamite.IO;
using Blamite.Serialization;
using Blamite.Util;
using System;
using System.IO;
using System.IO.Compression;

namespace Blamite.Compression
{
	public static class FirstGenXboxZLib
	{
		public static CompressionState AnalyzeCache(IReader reader, EngineDescription engineInfo, out StructureValueCollection headerValues)
		{
			reader.SeekTo(0);
			var headerLayout = engineInfo.Layouts.GetLayout("header");
			headerValues = StructureReader.ReadStructure(reader, headerLayout);

			var metaOffset = (int)headerValues.GetInteger("meta offset");

			if (metaOffset >= reader.Length)
				return CompressionState.Compressed;

			reader.SeekTo(metaOffset);
			StructureValueCollection tagTableValues = StructureReader.ReadStructure(reader, engineInfo.Layouts.GetLayout("meta header"));

			if ((uint)tagTableValues.GetInteger("magic") != CharConstant.FromString("tags"))
				return CompressionState.Compressed;

			return CompressionState.Decompressed;
		}

		public static void CompressCache(string cacheFile, int headerSize)
		{
			string tempFile = Path.GetTempFileName();

			using (var fsOutput = new FileStream(tempFile, FileMode.OpenOrCreate))
			using (var fsInput = new FileStream(cacheFile, FileMode.Open))
			using (var erInput = new EndianReader(fsInput, Endian.LittleEndian))
			{
				// Header is uncompressed
				fsOutput.Write(erInput.ReadBlock(headerSize), 0, headerSize);

				// Read data from 0x800 offset
				fsInput.Seek(0x800, SeekOrigin.Begin);
				int dataSize = (int)fsInput.Length - 0x800;
				byte[] chunkData = new byte[dataSize];
				int bytesRead = 0;
				while (bytesRead < dataSize)
				{
					int read = fsInput.Read(chunkData, bytesRead, dataSize - bytesRead);
					if (read == 0) break;
					bytesRead += read;
				}

				// Compress using ZLibStream (handles ZLib header + DEFLATE + Adler-32)
				using (var zlib = new ZLibStream(fsOutput, CompressionLevel.Optimal, true))
				{
					zlib.Write(chunkData, 0, bytesRead);
				}

				// CE Xbox pads to 0x800 alignment
				int remainder = (int)fsOutput.Length % 0x800;
				if (remainder != 0)
				{
					int padSize = 0x800 - remainder;
					fsOutput.Write(new byte[padSize], 0, padSize);
				}
			}

			File.Copy(tempFile, cacheFile, true);
			File.Delete(tempFile);
		}

		public static void DecompressCache(string cacheFile, int headerSize, int mapsize)
		{
			string tempFile = Path.GetTempFileName();

			using (var fsOutput = new FileStream(tempFile, FileMode.OpenOrCreate))
			using (var fsInput = new FileStream(cacheFile, FileMode.Open))
			using (var erInput = new EndianReader(fsInput, Endian.LittleEndian))
			{
				// Header is uncompressed
				fsOutput.Write(erInput.ReadBlock(headerSize), 0, headerSize);

				// Decompress using ZLibStream (handles ZLib header + DEFLATE + Adler-32)
				fsInput.Seek(headerSize, SeekOrigin.Begin);
				using (var zlib = new ZLibStream(fsInput, CompressionMode.Decompress, true))
				{
					zlib.CopyTo(fsOutput);
				}
			}

			File.Copy(tempFile, cacheFile, true);
			File.Delete(tempFile);
		}
	}
}
