using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Chip8Decompiler
{

	class Program
	{
		class Block
		{
			public int address;
			public int length;
			public HashSet<int> prev;
			public HashSet<int> next;

			public Block(int a)
			{
				address = a;
				length = -1;
				prev = new HashSet<int>();
				next = new HashSet<int>();
			}
		}

		struct Procedure
		{
			public int entryAddress;
			public int entryBlock;

			public Procedure(int eA)
			{
				entryAddress = eA;
				entryBlock = -1;
			}
		}

		private static bool isIf(byte[] program, int PC)
		{
			if (PC >= 0)
			{
				switch (((program[PC] << 8) | program[PC + 1]) & 0xF000)
				{
					case 0x3000:
						return true;
					case 0x4000:
						goto case 0x3000;
					case 0x5000:
						goto case 0x3000;
					case 0x9000:
						goto case 0x3000;
					case 0xE000:
						goto case 0x3000;
				}
			}

			return false;
		}

		private static int getNNN(int b0, int b1)
		{
			return (((b0 << 8) | b1) & 0xFFF) - 0x200;
		}
		
		private static int getBlock(List<Block> blockList, int address)
		{
			for(int i = 0; i < blockList.Count; ++i)
				if (blockList.ElementAt(i).address == address)
					return i;
			
			blockList.Add(new Block(address));

			return blockList.Count - 1;
		}

		private static bool isJump(byte[] program, int PC)
		{
			return ((program[PC] & 0xF0) == 0x10) ? true : false;
		}

		private static bool isReturn(byte[] program, int PC)
		{
			return ((program[PC] == 0) && (program[PC + 1]) == 0xEE) ? true : false;
		}

		private static int getNextIf(byte[] program, int PC)
		{
			for(; PC + 1 < program.Length; PC += 2)
			{
				switch (((program[PC] << 8) | program[PC + 1]) & 0xF000)
				{
					case 0x3000:
						return PC;
					case 0x4000:
						goto case 0x3000;
					case 0x5000:
						goto case 0x3000;
					case 0x9000:
						goto case 0x3000;
					case 0xE000:
						goto case 0x3000;
				}
			}

			return Int32.MaxValue;
		}

		private static int getNextJump(byte[] program, int PC, out bool c, out int dest) //Returns if it finds a JUMP, conditional JUMP, or RET
		{
			dest = Int32.MaxValue;
			for (; PC + 1 < program.Length; PC += 2)
			{
				c = isIf(program, PC - 2);
				
				if(program[PC + 1] == 0xEE)
				{
					return PC;
				}
				else if(isJump(program, PC))
				{
					dest = getNNN(program[PC], program[PC + 1]);
					return PC;
				}
			}

			c = false;
			return Int32.MaxValue;
		}
		
		private static List<Block> CreateBasicBlocks(byte[] program, Procedure cP)
		{
			List<Block> blockList = new List<Block>();
			Stack<int> workList = new Stack<int>();

			bool conditional;
			int currentAddress = cP.entryAddress;
			int blockNum = getBlock(blockList, currentAddress);
			int nextBlockNum, dest;
			cP.entryBlock = blockNum;
			workList.Push(currentAddress);

			//Find the starting addresses of each base
			while (workList.Count != 0)
			{
				currentAddress = workList.Pop();
				blockNum = getBlock(blockList, currentAddress);
				
				while (true)
				{
					int branch = getNextJump(program, currentAddress, out conditional, out dest);
					bool add = true;

					if (dest != Int32.MaxValue)
					{
						foreach (Block b in blockList)
						{
							if (b.address == dest)
							{
								add = false;
								break;
							}
						}

						if (add)
						{
							blockList.Add(new Block(dest));
							workList.Push(dest);
						}
					}

					if (conditional)
					{
						currentAddress = branch + 2;
						nextBlockNum = getBlock(blockList, currentAddress);
						blockNum = nextBlockNum;
					}
					else break;
				}
			}

			blockList.Sort((x, y) => x.address - y.address);
			currentAddress = cP.entryAddress;

			for(blockNum = 0; blockNum < blockList.Count; ++blockNum)
			{
				if (blockList.Count != blockNum + 1)
					blockList[blockNum].length = blockList[blockNum + 1].address - blockList[blockNum].address - 2;
				else
				{
					bool junk;
					int junk2, address = blockList[blockNum].address;
					blockList[blockNum].length = getNextJump(program, address, out junk, out junk2) - address;
				}
				int PC = blockList[blockNum].address + blockList[blockNum].length;
				
				if (isIf(program, PC - 2) || (!isJump(program, PC) && !isReturn(program, PC))) //If the last instruction is conditional, not a jump, or not a return
				{
					//Link to the next block
					blockList[blockNum].next.Add(blockNum + 1);
					blockList[blockNum + 1].prev.Add(blockNum);
				}
				if(isJump(program, PC)) //If the last instruction is a jump
				{
					int jumpBlock = getBlock(blockList, getNNN(program[PC], program[PC + 1]));

					blockList[blockNum].next.Add(jumpBlock);
					blockList[jumpBlock].prev.Add(blockNum);
				}
			}

			using (System.IO.StreamWriter file = new StreamWriter("gv.txt"))
			{
				int bN = 0;
				HashSet<string> arrows = new HashSet<string>();

				file.WriteLine("digraph {");
				file.WriteLine("\trankdir=\"LR\"");
				
				foreach (Block b in blockList)
				{
					arrows.Add(bN.ToString() + ";");

					Console.WriteLine(bN.ToString());
					Console.WriteLine("\tLength\t\t" + b.length.ToString());
					Console.Write("\tPrevious");
					foreach (int p in b.prev)
					{
						Console.Write("\t" + p.ToString());
						arrows.Add(p.ToString() + " -> " + bN.ToString() + ";");
					}
					Console.Write("\n\tNext\t");
					foreach (int n in b.next)
					{
						Console.Write("\t" + n.ToString());
						arrows.Add(bN.ToString() + " -> " + n.ToString() + ";");
					}
					Console.WriteLine();
					++bN;
				}

				foreach(string a in arrows)
					file.WriteLine("\t" + a);

				file.WriteLine("}");
			}
			
			return blockList;
		}

		static void Main(string[] args)
		{
			byte[] program = File.ReadAllBytes("pong2.c8");
			int PC = 0;

			bool r;
			int s;
			int t = getNextJump(program, 130, out r, out s);

			List<Procedure> procedureList = new List<Procedure>();
			procedureList.Add(new Procedure(PC));
			CreateBasicBlocks(program, procedureList.ElementAt(0));//For procedure make getProcedureAt()
		}
	}
}