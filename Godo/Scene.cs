﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Godo
{
    public class Scene
    {
        // Randomises the Scene.Bin
        public static byte[] RandomiseScene(byte[] data, byte[] camera, int sceneID, bool[] options, Random rnd, int[][][] jaggedModelAttackTypes, int seed, byte[] initCam)
        {
            /* Scene File Breakdown
             * The scene.bin comprises of 256 indvidual 'scene' files in a gzip format. Each scene contains 3 enemies and 4 formations.
             * The size of each scene is the same, as any unused data is padded with FF.
             */

            // Identifies where a try-catch was triggered in the scene
            string error = "";

            try
            {
                int[] enemyIDs = new int[8];                // 2 bytes per enemy ID, little endian so 260 would be 04 01 (104h), 3 enemies; includes 2 bytes of FF padding afterwards
                int[] battleSetup = new int[80];            // 4 records of 20 bytes each for Formations; Battle Setup Flags
                int[] cameraData = new int[192];            // 4 records of 38 bytes each for Formations; Camera Placement Data
                int[] formationPlacement = new int[384];    // 4 records of 96 bytes each for Formations; Enemy Placement Data (6 enemies per formation)
                int[] enemyData = new int[552];             // 3 records of 184 bytes each for Enemies; Enemy Data
                int[] attackData = new int[896];            // 32 records of 28 bytes each for Attacks; Enemy Attack Data
                int[] attackIDs = new int[64];              // 32 records of 2 bytes each for Attack IDs; Enemy Attack ID Data
                int[] attackNames = new int[1024];          // 32 records of 32 bytes each for Attack Names; Enemy Attack Name Data
                int[] formationAIOffset = new int[8];       // 8 bytes per formation AI script offset, 4 offsets
                int[] formationAI = new int[504];           // 504 bytes for Formation AI, 4 sets
                int[] enemyAIOffset = new int[6];           // 6 bytes per enemy AI script offset, 3 offsets
                int[] enemyAI = new int[4096];              // 4096 bytes for Enemy AI, 3 sets

                int rngID = 0; // Stores a randomly generated number

                int r = 0; // For iterating scene records (256 of them)
                int o = 0; // For iterating array indexes
                int c = 0; // For iterating records
                int k = 0; // See above

                byte battleBG = 0;

                bool validModel = false;
                bool excludedModel = false;
                bool enemyAnimGroup = false;
                bool bossAnimGroup = false;
                bool excludedScene = false;

                // Used to ascertain which models were swapped for which when writing to formation
                ulong enemyA = 0;
                ulong enemyB = 0;
                ulong enemyC = 0;

                byte[] nameBytes; // For assigning FF7 Ascii bytes after method processing
                                  //Random rnd = new Random(Guid.NewGuid().GetHashCode()); // TODO: Have it take a seed as argument

                // Two formations to handle 6 enemies each; A: two line, B: triangle
                ArrayList listedFormationData = new ArrayList();
                byte[] formationA =
                {
                    0xEC, 0xFA, 0x00, 0x00, 0x88, 0xFA, 0x01, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x50, 0xFB, 0x01, 0x00, 0x00, 0x00,
                    0x14, 0x05, 0x00, 0x00, 0x88, 0xFA, 0x01, 0x00, 0x00, 0x00,
                    0xEC, 0xFA, 0x00, 0x00, 0x10, 0xF5, 0x02, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x50, 0xFB, 0x02, 0x00, 0x00, 0x00,
                    0x14, 0x05, 0x00, 0x00, 0x10, 0xF5, 0x02, 0x00, 0x00, 0x00
                };

                byte[] formationB =
                {
                    0x00, 0x00, 0x00, 0x00, 0x50, 0xFB, 0x01, 0x00, 0x00, 0x00,
                    0x0C, 0xFE, 0x00, 0x00, 0x68, 0xF7, 0x01, 0x00, 0x00, 0x00,
                    0xF4, 0x01, 0x00, 0x00, 0x68, 0xF7, 0x01, 0x00, 0x00, 0x00,
                    0x18, 0xFC, 0x00, 0x00, 0x80, 0xF3, 0x02, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x80, 0xF3, 0x02, 0x00, 0x00, 0x00,
                    0xE8, 0x03, 0x00, 0x00, 0x80, 0xF3, 0x02, 0x00, 0x00, 0x00
                };

                listedFormationData.Add(formationA);
                listedFormationData.Add(formationB);
                int rand = (byte)rnd.Next(listedFormationData.Count);
                byte[] form = (byte[])listedFormationData[rand];

                #region Enemy IDs
                // Enemy IDs - Model Swap
                if (options[24] != false)
                {
                    while (r < 3)
                    {
                        // Don't want to add an enemy if there's none to begin with
                        if (data[o] != 255 && data[o + 1] != 255)
                        {
                            byte[] currentModelID = new byte[2];
                            currentModelID[0] = data[o];
                            currentModelID[1] = data[o + 1];
                            ulong currentModelIDInt = (ulong)AllMethods.GetLittleEndianIntTwofer(currentModelID, 0);

                            // Stores the original Model ID for potential use later in Battle Formation section
                            if (r == 0)
                            {
                                enemyA = currentModelIDInt;
                            }
                            else if (r == 1)
                            {
                                enemyB = currentModelIDInt;
                            }
                            else if (r == 2)
                            {
                                enemyC = currentModelIDInt;
                            }

                            // Models that are a dependency/have dependencies or otherwise shouldn't be changed
                            excludedModel = AllMethods.CheckExcludedModel(currentModelIDInt);

                            // If the enemy appears in an excluded scene, it isn't changed
                            excludedScene = AllMethods.CheckExcludedScene(sceneID);

                            // Boss group that have multiple idles/damaged animations and which have the same anim IDs for these
                            bossAnimGroup = AllMethods.CheckBossSet(currentModelIDInt);

                            // Enemies that support multiple idle/damaged animations and which have the same anim IDs for these
                            enemyAnimGroup = AllMethods.CheckAnimSet(currentModelIDInt);

                            while (validModel != true) // Checks that model ID assigned exists/is valid
                            {
                                if (excludedModel == true)
                                {
                                    // Does not change - Current Model is a dependency of some kind
                                    o += 2;
                                    validModel = true;
                                }
                                else if (excludedScene == true)
                                {
                                    // This scene is excluded from ModelID changes
                                    o += 2;
                                    validModel = true;
                                }
                                else if (bossAnimGroup == true)
                                {
                                    // Select a random index from Boss Anim Group
                                    ulong[] bossSet = { 10, 11, 22, 33, 37, 71, 81, 195 };
                                    ulong modelIDCheck = (ulong)rnd.Next(7);
                                    modelIDCheck = bossSet[modelIDCheck];
                                    byte[] model = AllMethods.GetLittleEndianConvert(modelIDCheck);
                                    data[o] = model[0]; o++;
                                    data[o] = model[1]; o++;
                                    validModel = true;
                                }
                                else if (enemyAnimGroup == true)
                                {
                                    // Select a random index from Enemy Anim Group
                                    ulong[] animSet = { 86, 131, 143, 147, 170, 202, 278, 339, 340, 341, 342, 343, 344, 347, 349, 350 };
                                    ulong modelIDCheck = (ulong)rnd.Next(15);
                                    modelIDCheck = animSet[modelIDCheck];
                                    byte[] model = AllMethods.GetLittleEndianConvert(modelIDCheck);
                                    data[o] = model[0]; o++;
                                    data[o] = model[1]; o++;
                                    validModel = true;
                                }         
                                else
                                {
                                    // If the filters all returned false, then a standard randomisation is performed
                                    ulong modelIDCheck = (ulong)rnd.Next(676);

                                    // Checks that the new ModelID doesn't match any of the filters or isn't present in the jagged array.
                                    // If any of the filters here return true, or the modelID doesn't exist in the jagged array, we loop through again.
                                    excludedModel = AllMethods.CheckExcludedModel(modelIDCheck);
                                    enemyAnimGroup = AllMethods.CheckAnimSet(modelIDCheck);
                                    bossAnimGroup = AllMethods.CheckBossSet(modelIDCheck);
                                    if (jaggedModelAttackTypes[modelIDCheck] != null && excludedModel != true && enemyAnimGroup != true && bossAnimGroup != true)
                                    {
                                        byte[] model = AllMethods.GetLittleEndianConvert(modelIDCheck);
                                        data[o] = model[0]; o++;
                                        data[o] = model[1]; o++;
                                        validModel = true;
                                    }
                                }
                            }
                            validModel = false;
                        }
                        else
                        {
                            // If enemy was FFFF then assume it was a null entry and proceed without changes
                            o += 2;
                        }
                        r++;
                    }
                }
                else
                {
                    // Model Swap turned OFF, so we just use the pre-existing Model IDs
                    o += 6;
                }

                // Stores the enemy IDs for use later in enforcing consistency
                byte[] enemyIDList = new byte[6];
                enemyIDList[0] = data[o - 6];
                enemyIDList[1] = data[o - 5];
                enemyIDList[2] = data[o - 4];
                enemyIDList[3] = data[o - 3];
                enemyIDList[4] = data[o - 2];
                enemyIDList[5] = data[o - 1];
                r = 0;

                // FF padding
                data[o] = 255; o++;
                data[o] = 255; o++;

                //array = enemyIDs.Select(b => (byte)b).ToArray();
                //bw.BaseStream.Position = 0x00000;
                //bw.Write(array, 0, array.Length);
                //o = 0;
                #endregion

                #region Battle Setup Flags
                error = "Battle Setup";
                while (r < 4)
                {
                    // If first value is FF (Battle BG), then assume it is an empty formation and skip
                    if (data[o] != 255)
                    {
                        // Battle BG/Location
                        if (options[25] != false)
                        {
                            data[o] = (byte)rnd.Next(89); o++; // ID of the Battle BG
                            battleBG = data[o - 1]; // Records battle BG value to check for supernova viability later
                            data[o] = data[o]; o++; // Always 0; despite being a 2-byte value, valid values never exceed 59h
                        }
                        else
                        {
                            // Retain the Battle BG
                            o += 2;
                        }

                        // Next Formation ID, this transitions to another enemy formation directly after current enemies defeated; like Battle Square but not random.
                        data[o] = data[o]; o++; // FFFF by default, no new battle will load
                        data[o] = data[o]; o++;

                        // Escape Counter; value of 0009 makes battle unescapable; 2-byte but value never exceeds 0009
                        if (options[26] != false)
                        {
                            data[o] = 9; o++;
                            data[o] = 0; o++;
                        }
                        else
                        {
                            o += 2;
                        }

                        // Unused - 2byte
                        data[o] = data[o]; o++;
                        data[o] = data[o]; o++;

                        // Battle Square - Possible Next Battles (4x 2-byte formation IDs, one is selected at random; default value for no battle is 03E7
                        data[o] = data[o]; o++; // Battle 1
                        data[o] = data[o]; o++;

                        data[o] = data[o]; o++; // Battle 2
                        data[o] = data[o]; o++;

                        data[o] = data[o]; o++; // Battle 3
                        data[o] = data[o]; o++;

                        data[o] = data[o]; o++; // Battle 4
                        data[o] = data[o]; o++;

                        // Escapable Flag (misc flags such as disabling pre-emptive)
                        data[o] = data[o]; o++;
                        data[o] = data[o]; o++; // Value of FDFF; FE F9 would prevent pre-emptives

                        // Battle Layout Type: Side attack, pincer, back attack, etc. 9 types.
                        /*
                            00 - Normal fight
                            01 - Preemptive
                            02 - Back attack
                            03 - Side attack
                            04 - Attacked from both sides (pincer attack, reverse side attack)
                            05 - Another attack from both sides battle (different maybe?)
                            06 - Another side attack
                            07 - A third side attack
                            08 - Normal battle that locks you in the front row, change command is disabled
                        */
                        data[o] = data[o]; o++;

                        if (options[27] != false)
                        {
                            // Indexed pre-battle camera position
                            // This is linked to the camera data, need to be careful what value is used
                            // Array has 4 bytes for the four formations, should iterate 4 times and no more.
                            data[o] = initCam[k]; o++; k++;
                        }
                        else
                        {
                            o++;
                        }
                    }
                    else
                    {
                        o += 20;
                        k++; // Initial Camera increment
                    }
                    r++;
                }
                r = 0;
                k = 0;

                //array = battleSetup.Select(b => (byte)b).ToArray();
                //bw.BaseStream.Position = 0x00008;
                //bw.Write(array, 0, array.Length);
                //o = 0;
                #endregion

                #region Camera Placement Data
                error = "Camera Placement";
                while (r < 4)
                {
                    if ((data[o] != 255 && data[o + 1] != 255) && options[27] != false)
                    {
                        // Using the byte array to retain camera data
                        // Primary Battle Idle Camera Position
                        data[o] = camera[k]; o++; k++; // Camera X Position
                        data[o] = camera[k]; o++; k++;

                        data[o] = camera[k]; o++; k++; // Camera Y Position
                        data[o] = camera[k]; o++; k++;

                        data[o] = camera[k]; o++; k++; // Camera Z Position
                        data[o] = camera[k]; o++; k++;

                        data[o] = camera[k]; o++; k++; // Focus X Direction
                        data[o] = camera[k]; o++; k++;

                        data[o] = camera[k]; o++; k++; // Focus Y Direction
                        data[o] = camera[k]; o++; k++;

                        data[o] = camera[k]; o++; k++;// Focus Z Direction
                        data[o] = camera[k]; o++; k++;


                        // Secondary Battle Idle Camera Position
                        data[o] = camera[k]; o++; k++; // Camera X Position
                        data[o] = camera[k]; o++; k++;

                        data[o] = camera[k]; o++; k++; // Camera Y Position
                        data[o] = camera[k]; o++; k++;

                        data[o] = camera[k]; o++; k++; // Camera Z Position
                        data[o] = camera[k]; o++; k++;

                        data[o] = camera[k]; o++; k++; // Focus X Direction
                        data[o] = camera[k]; o++; k++;

                        data[o] = camera[k]; o++; k++; // Focus Y Direction
                        data[o] = camera[k]; o++; k++;

                        data[o] = camera[k]; o++; k++; // Focus Z Direction
                        data[o] = camera[k]; o++; k++;


                        // Tertiary Battle Idle Camera Position
                        data[o] = camera[k]; o++; k++;// Camera X Position
                        data[o] = camera[k]; o++; k++;

                        data[o] = camera[k]; o++; k++;// Camera Y Position
                        data[o] = camera[k]; o++; k++;

                        data[o] = camera[k]; o++; k++; // Camera Z Position
                        data[o] = camera[k]; o++; k++;

                        data[o] = camera[k]; o++; k++;// Focus X Direction
                        data[o] = camera[k]; o++; k++;

                        data[o] = camera[k]; o++; k++;// Focus Y Direction
                        data[o] = camera[k]; o++; k++;

                        data[o] = camera[k]; o++; k++;// Focus Z Direction
                        data[o] = camera[k]; o++; k++;

                        // Unused Battle Camera Position - FF Padding
                        o += 12;
                    }
                    else
                    {    
                        // Skip and retain data
                        o += 48;
                    }
                    r++;
                    k = 0;
                }
                r = 0;

                //array = cameraData.Select(b => (byte)b).ToArray();
                //bw.BaseStream.Position = 0x00058;
                //bw.Write(array, 0, array.Length);
                //o = 0;
                #endregion

                #region Battle Formation Data
                error = "Battle Formation";
                if (excludedScene == true)
                {
                    // This scene is excluded, so skip the entire battle formation data and leave as-is
                    o += 64;
                }
                else
                {
                    while (r < 4)
                    {
                        // First, to allocate the Enemy IDs to the 6 possible formation slots.
                        // This is complicated by Model Swapping and the Enemy Swarm functions.
                        // First, we do the Model Swap (if any exist).
                        // Next, we do the enemy swarm function which looks at the current Model IDs and uses those.
                        while (c < 6)
                        {
                            // Checks that the current enemy placement entry is not null
                            if (data[o] != 255 && data[o + 1] != 255)
                            {
                                // If we changed the enemy IDs, then we need to match the old IDs to the new ones
                                // so that we're replacing the references to them correctly in the formation

                                // We get the current Model ID first from this formation entry
                                byte[] currentModelID = new byte[2];
                                currentModelID[0] = data[o];
                                currentModelID[1] = data[o + 1];
                                ulong currentModelIDInt = (ulong)AllMethods.GetLittleEndianIntTwofer(currentModelID, 0);

                                // It gets compared to the original Model IDs that we collected at the start before
                                // randomisation to determine if it is Enemy A, B, or C.
                                if (currentModelIDInt == enemyA)
                                {
                                    data[o] = enemyIDList[0]; o++;
                                    data[o] = enemyIDList[1]; o++;
                                }
                                else if (currentModelIDInt == enemyB)
                                {
                                    // This can't be null, because the check at start would have hit it
                                    // So the commented logic shouldn't be needed after all
                                    data[o] = enemyIDList[2]; o++;
                                    data[o] = enemyIDList[3]; o++;

                                    //if (enemyIDList[2] != 255 && enemyIDList[3] != 255)
                                    //{
                                    //    data[o] = enemyIDList[2]; o++;
                                    //    data[o] = enemyIDList[3]; o++;
                                    //}
                                    //else if (options[29] != false)
                                    //{
                                    //    data[o] = enemyIDList[0]; o++;
                                    //    data[o] = enemyIDList[1]; o++;
                                    //}
                                    //else
                                    //{
                                    //    o += 2;
                                    //}
                                }
                                else if (currentModelIDInt == enemyC)
                                {
                                    // Same here
                                    data[o] = enemyIDList[4]; o++;
                                    data[o] = enemyIDList[5]; o++;

                                    //if (enemyIDList[4] != 255 && enemyIDList[5] != 255)
                                    //{
                                    //    data[o] = enemyIDList[4]; o++;
                                    //    data[o] = enemyIDList[5]; o++;
                                    //}
                                    //else if (options[29] != false)
                                    //{
                                    //    data[o] = enemyIDList[0]; o++;
                                    //    data[o] = enemyIDList[1]; o++;
                                    //}
                                    //else
                                    //{
                                    //    o += 2;
                                    //}
                                }
                                else
                                {
                                    // Enemy Swarm
                                    if (options[29] != false)
                                    {
                                        // We can assume that the first enemy is never null, very few scenes have Enemy A as no entry
                                        // And those that do are unused
                                        data[o] = enemyIDList[0]; o++;
                                        data[o] = enemyIDList[1]; o++;
                                    }
                                    else
                                    {
                                        // If we got here, the Enemy ID for this formation wasn't null and yet did not match with Enemy A, B, or C + Enemy Swarm was disabled
                                        MessageBox.Show("Logical error occurred in Scene ID: " + sceneID + " for Battle Formation Line 500 - Please report bug to Sega Chief if seen.");
                                        o += 2;
                                    }
                                }

                                // XYZ + Row/Cover Flags for the enemy, populated by a pre-built array from Indexer.cs
                                // Is only used if Enemy Swarm is enabled for giving the new enemies coords
                                if (options[29] != false)
                                {
                                    // X Coordinate
                                    data[o] = form[k]; o++; k++;
                                    data[o] = form[k]; o++; k++;

                                    // Y Coordinate
                                    data[o] = form[k]; o++; k++;
                                    data[o] = form[k]; o++; k++;

                                    // Z Coordinate
                                    data[o] = form[k]; o++; k++;
                                    data[o] = form[k]; o++; k++;

                                    // Row
                                    data[o] = form[k]; o++; k++;
                                    data[o] = form[k]; o++; k++;

                                    // Cover Flags (should be related to Row)
                                    data[o] = form[k]; o++; k++;
                                    data[o] = form[k]; o++; k++;
                                }
                                else
                                {
                                    // X Coordinate
                                    data[o] = data[o]; o++;
                                    data[o] = data[o]; o++;

                                    // Y Coordinate
                                    data[o] = data[o]; o++;
                                    data[o] = data[o]; o++;

                                    // Z Coordinate
                                    data[o] = data[o]; o++;
                                    data[o] = data[o]; o++;

                                    // Row
                                    data[o] = data[o]; o++;
                                    data[o] = data[o]; o++;

                                    // Cover Flags
                                    data[o] = data[o]; o++;
                                    data[o] = data[o]; o++;
                                }

                                // Initial Condition Flags; only the last 5 bits are considered - FF FF FF FF is default

                                // Best this is disabled and it just retains what's there, prevents issues
                                //excludedModel = AllMethods.CheckExcludedModel(currentModelIDInt);
                                //enemyAnimGroup = AllMethods.CheckAnimSet(currentModelIDInt);
                                //bossAnimGroup = AllMethods.CheckBossSet(currentModelIDInt);
                                //if (excludedModel != true && enemyAnimGroup != true && bossAnimGroup != true)
                                //{
                                //    data[o] = 255; o++;
                                //    data[o] = 255; o++;
                                //    data[o] = 255; o++;
                                //    data[o] = 255; o++;
                                //}
                                //else
                                //{
                                    data[o] = data[o]; o++;
                                    data[o] = data[o]; o++;
                                    data[o] = data[o]; o++;
                                    data[o] = data[o]; o++;
                                //}
                            }
                            // If Enemy Swarm is enabled, we attempt to add a new enemy here as the current entry is null
                            // We don't want to do this, however, if the Boss flag was enabled at any point
                            else if (options[29] != false && bossAnimGroup == false)
                            {
                                // For now, just going to add Enemy A as the duped enemy.
                                // Later, will revive the RND for Enemy A, B, C
                                data[o] = enemyIDList[0]; o++;
                                data[o] = enemyIDList[1]; o++;

                                //// Set the rng so that a null enemy can't be picked for the new entry
                                //if (enemyData[2] == 255 && enemyData[3] == 255)
                                //{
                                //    rngID = 0;
                                //}
                                //else if (enemyData[4] == 255 && enemyData[5] == 255)
                                //{
                                //    rngID = rnd.Next(2);
                                //}
                                //else
                                //{
                                //    rngID = rnd.Next(3);
                                //}

                                //// Pick a random enemy
                                //if (rngID == 0)
                                //{
                                //    // Sets enemy A as the formation enemy ID
                                //    data[o] = enemyIDList[0]; o++;
                                //    data[o] = enemyIDList[1]; o++;
                                //}
                                //else if (rngID == 1)
                                //{
                                //    // Sets enemy B as the formation enemy ID
                                //    data[o] = enemyIDList[2]; o++;
                                //    data[o] = enemyIDList[3]; o++;
                                //}
                                //else if(rngID == 2)
                                //{
                                //    // Sets enemy C as the formation enemy ID
                                //    data[o] = enemyIDList[4]; o++;
                                //    data[o] = enemyIDList[5]; o++;
                                //}
                                //else
                                //{
                                //    data[o] = enemyIDList[0]; o++;
                                //    data[o] = enemyIDList[1]; o++;
                                //}


                                // X Coordinate
                                data[o] = form[k]; o++; k++;
                                data[o] = form[k]; o++; k++;

                                // Y Coordinate
                                data[o] = form[k]; o++; k++;
                                data[o] = form[k]; o++; k++;

                                // Z Coordinate
                                data[o] = form[k]; o++; k++;
                                data[o] = form[k]; o++; k++;

                                // Row
                                data[o] = form[k]; o++; k++;
                                data[o] = form[k]; o++; k++;

                                // Cover Flags (should be related to Row)
                                data[o] = form[k]; o++; k++;
                                data[o] = form[k]; o++; k++;

                                // Initial Condition Flags; only the last 5 bits are considered - FF FF FF FF is default

                                // As above, disabling this as it isn't a good idea - Retain flags instead
                                //byte[] currentModelID = new byte[2];
                                //currentModelID[0] = data[o];
                                //currentModelID[1] = data[o + 1];
                                //ulong currentModelIDInt = (ulong)AllMethods.GetLittleEndianIntTwofer(currentModelID, 0);

                                //excludedModel = AllMethods.CheckExcludedModel(currentModelIDInt);
                                //enemyAnimGroup = AllMethods.CheckAnimSet(currentModelIDInt);
                                //bossAnimGroup = AllMethods.CheckBossSet(currentModelIDInt);
                                //if (excludedModel != true && enemyAnimGroup != true && bossAnimGroup != true)
                                //{
                                //    data[o] = 255; o++;
                                //    data[o] = 255; o++;
                                //    data[o] = 255; o++;
                                //    data[o] = 255; o++;
                                //}
                                //else
                                //{
                                    data[o] = data[o]; o++;
                                    data[o] = data[o]; o++;
                                    data[o] = data[o]; o++;
                                    data[o] = data[o]; o++;
                                //}
                            }
                            else
                            {
                                // Entry left unchanged
                                o += 16;
                                k += 10;
                            }
                        }
                        k = 0;
                        r++;
                    }
                }
                r = 0;
                //array = formationPlacement.Select(b => (byte)b).ToArray();
                //bw.BaseStream.Position = 0x00118;
                //bw.Write(array, 0, array.Length);
                //o = 0;
                #endregion

                #region Enemy Data
                error = "Enemy Data";
                while (r < 3)
                {
                    int i = 0;

                    // If first byte is empty, assume no enemy is there and just retain pre-existing data
                    if (data[o] != 255)
                    {
                        // Enemy Name option
                        if (options[30] != false)
                        {
                            // Enemy Name, 32 bytes ascii
                            nameBytes = AllMethods.NameGenerate(rnd);
                            data[o] = nameBytes[0]; o++;
                            data[o] = nameBytes[1]; o++;
                            data[o] = nameBytes[2]; o++;
                            data[o] = nameBytes[3]; o++;

                            rngID = rnd.Next(2); // Chance to append a longer name
                            if (rngID == 1)
                            {
                                data[o] = nameBytes[4]; o++;
                                data[o] = nameBytes[5]; o++;
                                data[o] = nameBytes[6]; o++;
                                data[o] = nameBytes[7]; o++;

                                while (i < 23)
                                {
                                    data[o] = 255; o++;
                                    i++;
                                }
                                data[o] = 255; o++; // Empty - Use FF to terminate the string
                                i = 0;
                            }
                            else
                            {
                                while (i < 27)
                                {
                                    data[o] = 255; o++;
                                    i++;
                                }
                                data[o] = 255; o++; // Empty - Use FF to terminate the string
                                i = 0;
                            }
                        }
                        else
                        {
                            // Keeping default name
                                o += 32;
                        }


                        // Enemy Stats
                        if (options[31] != false)
                        {
                            if(sceneID < 74)
                            {
                                // World Map Encounters
                                byte statWorldMax = (byte)(sceneID + rnd.Next(15, 25));
                                byte statWorldMin = (byte)(sceneID / rnd.Next(4));

                                // Enemy Level
                                data[o] = (byte)rnd.Next(statWorldMin, statWorldMax); o++;

                                // Enemy Speed
                                data[o] = (byte)rnd.Next(statWorldMin, 128); o++;

                                // Enemy Luck
                                data[o] = (byte)rnd.Next(0, statWorldMin); o++;

                                // Enemy Evade
                                data[o] = (byte)rnd.Next(0, statWorldMin); o++;

                                // Enemy StrengthstatAdjustMax
                                data[o] = (byte)rnd.Next(statWorldMin, statWorldMax); o++;

                                // Enemy Defence
                                data[o] = (byte)rnd.Next(statWorldMin, statWorldMax); o++;

                                // Enemy Magic
                                data[o] = (byte)rnd.Next(statWorldMin, statWorldMax); o++;

                                // Enemy Magic Defence
                                data[o] = (byte)rnd.Next(statWorldMin, statWorldMax); o++;
                            }
                            else if (bossAnimGroup == true)
                            {
                                // Boss parameters
                                byte levelBossMax = (byte)(sceneID - 60);
                                byte levelBossMin = (byte)(sceneID - 75);
                                byte atkBossMax = (byte)(sceneID - 30);
                                byte atkBossMin = (byte)(sceneID - 45);
                                byte magBossMax = (byte)(sceneID - 60 + (sceneID / 8));
                                byte magBossMin = (byte)(sceneID - 70 + (sceneID / 12));
                                byte defBossMax = (byte)(sceneID - 60);
                                byte defBossMin = (byte)(sceneID - 70);

                                // Enemy Level
                                data[o] = (byte)rnd.Next(levelBossMin, levelBossMax); o++;

                                // Enemy Speed
                                data[o] = (byte)rnd.Next(48, 127); o++;

                                // Enemy Luck
                                data[o] = (byte)rnd.Next(0, 32); o++;

                                // Enemy Evade
                                data[o] = (byte)rnd.Next(0, 16); o++;

                                // Enemy Strength
                                data[o] = (byte)rnd.Next(atkBossMin, atkBossMax); o++;

                                // Enemy Defence
                                data[o] = (byte)rnd.Next(defBossMin, defBossMax); o++;

                                // Enemy Magic
                                data[o] = (byte)rnd.Next(magBossMin, magBossMax); o++;

                                // Enemy Magic Defence
                                data[o] = (byte)rnd.Next(defBossMin, defBossMax); o++;
                            }
                            else
                            {
                                // Field enemy parameters
                                byte levelFieldMax = (byte)(sceneID - 70);
                                byte levelFieldMin = (byte)(sceneID - 75);
                                byte atkFieldMax = (byte)(sceneID - 60);
                                byte atkFieldMin = (byte)(sceneID - 70);
                                byte magFieldMax = (byte)(sceneID - 60 + (sceneID / 12));
                                byte magFieldMin = (byte)(sceneID - 70 + (sceneID / 16));
                                byte defenceFieldMax = (byte)(sceneID - 65);
                                byte defenceFieldMin = (byte)(sceneID - 70);

                                // Enemy Level
                                data[o] = (byte)rnd.Next(levelFieldMin, levelFieldMax); o++;

                                // Enemy Speed
                                data[o] = (byte)rnd.Next(24, 127); o++;

                                // Enemy Luck
                                data[o] = (byte)rnd.Next(0, 32); o++;

                                // Enemy Evade
                                data[o] = (byte)rnd.Next(0, 16); o++;

                                // Enemy StrengthstatAdjustMax
                                data[o] = (byte)rnd.Next(atkFieldMin, atkFieldMax); o++;

                                // Enemy Defence
                                data[o] = (byte)rnd.Next(defenceFieldMin, defenceFieldMax); o++;

                                // Enemy Magic
                                data[o] = (byte)rnd.Next(atkFieldMin, atkFieldMax); o++;

                                // Enemy Magic Defence
                                data[o] = (byte)rnd.Next(defenceFieldMin, defenceFieldMax); o++;
                            }
                        }
                        else
                        {
                            // No Stat changes at this time
                            o += 8;
                        }

                        // Stronger Enemies option
                        if (options[47] != false)
                        {
                            // We go back 8 places to reroll the stats
                            o -= 8;

                            // We perform checks to avoid going over 255
                            // Gaze upon my ternarys, ye almighty, and despair

                            // Enemy Level
                            data[o] = (data[o] <= 212) ? (byte)(data[o] * 1.2) : data[o] = 255; o++;

                            // Enemy Speed
                            data[o] = (data[o] <= 225) ? (byte)(data[o] + 30) : data[o] = 255; o++;

                            // Enemy Luck
                            data[o] = (data[o] <= 225) ? (byte)(data[o] + 30) : data[o] = 255; o++;

                            // Enemy Evasion
                            data[o] = (data[o] <= 245) ? (byte)(data[o] + 10) : data[o] = 255; o++;

                            // Enemy Strength
                            data[o] = (data[o] <= 230) ? (byte)(data[o] + 25) : data[o] = 255; o++;

                            // Enemy Defence
                            data[o] = (data[o] <= 225) ? (byte)(data[o] + 30) : data[o] = 255; o++;

                            // Enemy Magic
                            data[o] = (data[o] <= 212) ? (byte)(data[o] * 1.2) : data[o] = 255; o++;

                            // Enemy Magic Defence
                            data[o] = (data[o] <= 212) ? (byte)(data[o] * 1.2) : data[o] = 255; o++;
                        }
                        else if (options[48] != false)
                        {
                            // We go back 8 places to reroll the stats
                            o -= 8;

                            // Enemy Level
                            data[o] = (byte)(data[o] * 0.75); o++;

                            // Enemy Speed
                            data[o] = (byte)(data[o] * 0.50); o++;

                            // Enemy Luck
                            data[o] = 0; o++;

                            // Enemy Evasion
                            data[o] = 0; o++;

                            // Enemy Strength
                            data[o] = (byte)(data[o] * 0.75); o++;

                            // Enemy Defence
                            data[o] = (byte)(data[o] * 0.25); o++;

                            // Enemy Magic
                            data[o] = (byte)(data[o] * 0.75); o++;

                            // Enemy Magic Defence
                            data[o] = (byte)(data[o] * 0.25); o++;
                        }

                        // Enemy Elemental Types
                        /*
                            00h - Fire
                            01h - Ice
                            02h - Bolt
                            03h - Earth
                            04h - Bio
                            05h - Gravity
                            06h - Water
                            07h - Wind
                            08h - Holy
                            09h - Health
                            0Ah - Cut
                            0Bh - Hit
                            0Ch - Punch
                            0Dh - Shoot
                            0Eh - Scream
                            0Fh - HIDDEN
                            10h-1Fh - No Effect
                            20h-3Fh - Statuses (Damage done by actions that inflict these statuses will be modified)
                            FFh - No element
                        */
                        if (options[32] != false)
                        {
                            // 2 elemental properties have been set (may include statuses; unused FF7 thing where you take 2x damage while under a status)
                            data[o] = (byte)rnd.Next(0, 17); o++;
                            data[o] = (byte)rnd.Next(0, 17); o++;
                            data[o] = 255; o++;
                            data[o] = 255; o++;
                            data[o] = 255; o++;
                            data[o] = 255; o++;
                            data[o] = 255; o++;
                            data[o] = 255; o++;

                            // Elemental Rates/Modifiers
                            /*
                                00h - Death
                                02h - Double Damage
                                03h - Unknown
                                04h - Half Damage
                                05h - Nullify Damage
                                06h - Absorb 100%
                                07h - Full Cure
                                FFh - Nothing
                             */
                            data[o] = (byte)rnd.Next(1, 7); o++;
                            data[o] = (byte)rnd.Next(1, 7); o++;
                            data[o] = 255; o++;
                            data[o] = 255; o++;
                            data[o] = 255; o++;
                            data[o] = 255; o++;
                            data[o] = 255; o++;
                            data[o] = 255; o++;
                        }
                        else
                        {
                            // No change to elemental affinities
                            o += 16;
                        }

                        // Action Animation Index
                        /* 
                         * This is the most complex and sensitive part of the scene randomisation, if Model Swap is enabled
                         * 1) Grabs the associated AttackID from further down
                         * Check that attack's data (what is it using; impact, spell, neither)
                         * Assign Animation Indice based on a random value within the JaggedModelAttackType's ModelID's attack type container
                         */
                        if (options[24] != false)
                        {
                            // Attack ID > Attack Type - An Array of this scene's attacks and whether they are physical, magical, or misc
                            int[][] jaggedAttackType = new int[1280][];
                            int y = 0;

                            // Iterate through all 32 entries for attacks in this scene
                            while (c < 32)
                            {
                                byte[] attackID = new byte[2];
                                int type;

                                // Checks AttackID isn't blank and then takes it, converts it into Int for array index
                                if (data[2113 + k] != 255)
                                {
                                    attackID = data.Skip(2112 + k).Take(2).ToArray();
                                    int attackIDInt = AllMethods.GetLittleEndianIntTwofer(attackID, 0);

                                    // Checks anim and impact to determine attack type
                                    if (data[1217 + y] != 255)
                                    {
                                        type = 0; // Assigns this AttackID as Physical
                                        jaggedAttackType[attackIDInt] = new int[] { type };
                                    }
                                    else if (data[1229 + y] != 255)
                                    {
                                        type = 1; // Assigns this AttackID as Magic
                                        jaggedAttackType[attackIDInt] = new int[] { type };
                                    }
                                    else
                                    {
                                        type = 2; // Assigns this AttackID as Misc
                                        jaggedAttackType[attackIDInt] = new int[] { type };
                                    }
                                }
                                c++;
                                k += 2; // Attack ID in the list - wait, shouldn't this be from the actual attack data itself, not the enemy list? May have found the source of error.
                                // Attack IDs aren't organised necessarily in an enemy's list and may omit some of them if not all being used. That's how wrong data is getting assigned
                                // to wrong attacks. If enemy doesn't use all the attacks, then it assigns to a different one. This is priority 1 for fix.
                                y += 28; // Jumps to next attack to get impact/spell anim info - above should be doing same jump, now that I think about it
                            }
                            c = 0;
                            k = 0;
                            y = 0;

                            while (c < 16)
                            {
                                byte[] modelID = new byte[2];
                                if (r == 0)
                                {
                                    modelID[0] = enemyIDList[0];
                                    modelID[1] = enemyIDList[1];
                                }
                                else if (r == 1)
                                {
                                    modelID[0] = enemyIDList[2];
                                    modelID[1] = enemyIDList[3];
                                }
                                else if (r == 2)
                                {
                                    modelID[0] = enemyIDList[4];
                                    modelID[1] = enemyIDList[5];
                                }

                                int modelIDInt = AllMethods.GetLittleEndianIntTwofer(modelID, 0);

                                byte[] attackID = new byte[2];
                                attackID = data.Skip(736 + y).Take(2).ToArray();
                                int attackIDInt = AllMethods.GetLittleEndianIntTwofer(attackID, 0);
                                int anim = 0;
                                int first = 0;
                                int terminate = 0;

                                if (attackIDInt != 65535)
                                {
                                    if (jaggedAttackType[attackIDInt][0] == 0)
                                    {
                                        while (first == 0 || jaggedModelAttackTypes[modelIDInt][0][anim] == 0)
                                        {
                                            first++;
                                            anim = rnd.Next(0, jaggedModelAttackTypes[modelIDInt][0].Length);
                                            terminate++;
                                            if (terminate > 32)
                                            {
                                                break;
                                            }
                                        }
                                        if (terminate < 32)
                                        {
                                            data[o] = (byte)jaggedModelAttackTypes[modelIDInt][0][anim];
                                        }
                                        o++;
                                        first = 0;
                                    }
                                    else if (jaggedAttackType[attackIDInt][0] == 1)
                                    {
                                        while (first == 0 || jaggedModelAttackTypes[modelIDInt][1][anim] == 0)
                                        {
                                            first++;
                                            anim = rnd.Next(0, jaggedModelAttackTypes[modelIDInt][1].Length);
                                            terminate++;
                                            if (terminate > 32)
                                            {
                                                break;
                                            }
                                        }
                                        if (terminate < 32)
                                        {
                                            data[o] = (byte)jaggedModelAttackTypes[modelIDInt][1][anim];
                                        }
                                        o++;
                                        first = 0;
                                    }
                                    else if (jaggedAttackType[attackIDInt][0] == 2)
                                    {
                                        while (first == 0 || jaggedModelAttackTypes[modelIDInt][2][anim] == 0)
                                        {
                                            first++;
                                            anim = rnd.Next(0, jaggedModelAttackTypes[modelIDInt][2].Length);
                                            terminate++;
                                            if (terminate > 32)
                                            {
                                                break;
                                            }
                                        }
                                        if (terminate < 32)
                                        {
                                            data[o] = (byte)jaggedModelAttackTypes[modelIDInt][2][anim];
                                        }
                                        o++;
                                        first = 0;
                                    }
                                    else
                                    {
                                        // If this was hit, something is wrong with animation setting in jaggedAttackType of Indexer
                                        data[o] = data[o]; o++;
                                    }

                                    //if (terminate == 32)
                                    //{
                                    //    // We have a ModelID that does not have a required animation; we must re-roll this scene
                                    //    reroll = 0;
                                    //}
                                }
                                else
                                {
                                    o++;
                                }
                                anim = 0;
                                terminate = 0;
                                y += 2;
                                c++;
                            }
                            c = 0;
                            //Array.Clear(jaggedAttackType, 0, jaggedAttackType.Length);
                        }
                        else
                        {
                            o += 16;
                        }

                        // Enemy Attack IDs for matching to Animation IDs - 2bytes per attack ID
                        while (c < 16)
                        {
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                            c++;
                        }
                        c = 0;

                        // Enemy Camera Override IDs for matching to Animation IDs - 2bytes per Camera Override ID - FFFF by default
                        while (c < 16)
                        {
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                            c++;
                        }
                        c = 0;

                        // Obtain Rates
                        // 1 byte per item, 4 items. Values below 80 are Drop Items (#/63). Values above 80 are Steal Items (#63)
                        if (options[33] != false)
                        {
                            // Steal Rate 1-4
                            data[o] = (byte)rnd.Next(8, 63); o++; // Item 1
                            data[o] = (byte)rnd.Next(88, 127); o++; // Item 2
                            data[o] = 255; o++; // Item 3
                            data[o] = 255; o++; // Item 4

                            // Item IDs to be matched to the above drop/steal rates
                            // Prevents the setting of empty item IDs.
                            ulong itemIDInt = 105;
                            while (itemIDInt > 104 && itemIDInt < 128)
                            {
                                itemIDInt = (ulong)rnd.Next(320);
                            }
                            byte[] converted = AllMethods.GetLittleEndianConvert(itemIDInt);
                            byte first = converted[0];
                            byte second = converted[1];

                            // Item 1
                            data[o] = first; o++;
                            data[o] = second; o++;

                            // Prevents the setting of empty item IDs.
                            itemIDInt = 105;
                            while (itemIDInt > 104 && itemIDInt < 128)
                            {
                                itemIDInt = (ulong)rnd.Next(320);
                            }
                            converted = AllMethods.GetLittleEndianConvert(itemIDInt);
                            first = converted[0];
                            second = converted[1];
                            // Item 2
                            data[o] = first; o++;
                            data[o] = second; o++;

                            // Item 3 & 4 are unchanged
                            o += 4;
                        }
                        else if (options[49] != false)
                        {
                            // This needs to check if it is a drop or steal, then set max chance
                            if (data[o] != 255 && data[o] < 64)
                            {
                                data[o] = 63; o++;
                            }
                            else if (data[o] != 255 && data[o] > 63)
                            {
                                data[o] = 127;// whatever max steal rate is
                            }
                            else
                            {
                                data[o] = 255; o++;
                            }
                            // then do other 3 item rates
                        }
                        else if (options[46] != false)
                        {
                            // Rates
                            data[o] = 255; o++;
                            data[o] = 255; o++;
                            data[o] = 255; o++;
                            data[o] = 255; o++;

                            // Item 1-4
                            data[o] = 255; o++;
                            data[o] = 255; o++;

                            data[o] = 255; o++;
                            data[o] = 255; o++;

                            data[o] = 255; o++;
                            data[o] = 255; o++;

                            data[o] = 255; o++;
                            data[o] = 255; o++;
                        }
                        else
                        {
                            // Rates
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;

                            // Item 1-4
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;

                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;

                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;

                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                        }

                        // Manipulate/Berserk Attack IDs
                        // The first listed attack is the Berserk option; all 3 attacks can be selected for use under Manipulate
                        data[o] = data[o]; o++;
                        data[o] = data[o]; o++;

                        data[o] = data[o]; o++;
                        data[o] = data[o]; o++;

                        data[o] = data[o]; o++;
                        data[o] = data[o]; o++;

                        // Unknown Data
                        data[o] = 255; o++;
                        data[o] = 255; o++;

                        // Enemy MP
                        if (options[50] != false)
                        {
                            data[o] = 0; o++;
                            data[o] = 0; o++;
                        }
                        else if (options[34] != false)
                        {
                            if (bossAnimGroup == true)
                            {
                                data[o] = 11; o++;
                                data[o] = 184; o++;
                            }
                            else
                            {
                                data[o] = (byte)rnd.Next(0, 11); o++;
                                data[o] = (byte)rnd.Next(0, 184); o++;
                            }
                        }
                        else
                        {
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                        }

                        // Enemy AP
                        if (options[59] != false)
                        {
                            data[o] = 0; o++;
                            data[o] = 0; o++;
                        }
                        else if (options[46] != false)
                        {
                            data[o] = data[o]; o++;
                            data[o] = 0; o++;
                        }
                        else if (options[35] != false)
                        {
                            if (bossAnimGroup == true)
                            {
                                data[o] = 0; o++;
                                data[o] = 4; o++;
                            }
                            else
                            {
                                data[o] = (byte)rnd.Next(0, statAdjustMin); o++;
                                data[o] = 0; o++;
                            }
                        }
                        else
                        {
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                        }

                        // Enemy Morph Item ID - FFFF means no morph
                        if (options[33] != false)
                        {
                            // Prevents the setting of empty item IDs.
                            ulong itemIDInt = 105;
                            while (itemIDInt > 104 && itemIDInt < 128)
                            {
                                itemIDInt = (ulong)rnd.Next(320);
                            }
                            byte[] converted = AllMethods.GetLittleEndianConvert(itemIDInt);
                            byte first = converted[0];
                            byte second = converted[1];
                            data[o] = first; o++;
                            data[o] = second; o++;
                        }
                        else if (options[46] != false)
                        {
                            data[o] = 255; o++;
                            data[o] = 255; o++;
                        }
                        else
                        {
                            o += 2;
                        }

                        // Back Attack multiplier
                        if (options[31] != false)
                        {
                            data[o] = (byte)rnd.Next(0, 33); o++;
                        }
                        else
                        {
                            o++;
                        }

                        // Alignment FF
                        data[o] = 255; o++;

                        // Enemy HP
                        if (options[36] != false)
                        {
                            if (bossAnimGroup == true)
                            {
                                data[o] = (byte)rnd.Next(0, 256); o++;
                                data[o] = (byte)(hpAdjust + 2); o++;
                                data[o] = 0; o++;
                                data[o] = 0; o++;
                            }
                            else
                            {
                                data[o] = (byte)rnd.Next(0, 256); o++;
                                data[o] = hpAdjust; o++;
                                data[o] = 0; o++;
                                data[o] = 0; o++;
                            }
                        }
                        else
                        {
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                        }

                        // EXP Points
                        if(options[57] != false)
                        {
                            // No EXP
                            data[o] = 0; o++;
                            data[o] = 0; o++;
                            data[o] = 0; o++;
                            data[o] = 0; o++;
                        }
                        else if (options[46] != false)
                        {
                            // Poverty Mode EXP
                            data[o] = data[o]; o++;
                            data[o] = 0; o++;
                            data[o] = 0; o++;
                            data[o] = 0; o++;
                        }
                        else if (options[37] != false)
                        {
                            // Randomised EXP
                            if (bossAnimGroup == true)
                            {
                                data[o] = (byte)rnd.Next(0, 256); o++;
                                data[o] = expAdjust; o++;
                                data[o] = 0; o++;
                                data[o] = 0; o++;
                            }
                            else
                            {
                                data[o] = (byte)rnd.Next(0, 256); o++;
                                data[o] = (byte)rnd.Next(0, 2); o++;
                                data[o] = 0; o++;
                                data[o] = 0; o++;
                            }
                        }
                        else
                        {
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                        }

                        // Gil
                        if(options[58] != false)
                        {
                            // No Gil
                            data[o] = 0; o++;
                            data[o] = 0; o++;
                            data[o] = 0; o++;
                            data[o] = 0; o++;
                        }
                        else if (options[46] != false)
                        {
                            // Poverty Mode Gil
                            data[o] = data[o]; o++;
                            data[o] = 0; o++;
                            data[o] = 0; o++;
                            data[o] = 0; o++;
                        }
                        else if (options[38] != false)
                        {
                            // Randomised Gil
                            if (bossAnimGroup == true)
                            {
                                data[o] = (byte)rnd.Next(0, 256); o++;
                                data[o] = expAdjust; o++;
                                data[o] = 0; o++;
                                data[o] = 0; o++;
                            }
                            else
                            {
                                data[o] = (byte)rnd.Next(0, 256); o++;
                                data[o] = (byte)rnd.Next(0, 2); o++;
                                data[o] = 0; o++;
                                data[o] = 0; o++;
                            }
                        }
                        else
                        {
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                        }

                        int picker = rnd.Next(4);
                        int[] status = new int[] { 1, 2, 4, 8, 16, 32, 64, 128 };

                        // Status Immunities
                        if (options[39] != false)
                        {
                            if (bossAnimGroup == true)
                            {
                                if (picker == 0)
                                {
                                    picker = rnd.Next(2, 8); // Prevents Death
                                    data[o] = (byte)status[picker]; o++;
                                    data[o] = 0; o++;
                                    data[o] = 0; o++;
                                    data[o] = 0; o++;

                                }
                                else if (picker == 1)
                                {
                                    picker = rnd.Next(8);
                                    data[o] = 0; o++;
                                    data[o] = (byte)(status[picker]); o++;
                                    data[o] = 0; o++;
                                    data[o] = 0; o++;
                                }
                                else if (picker == 2)
                                {
                                    picker = rnd.Next(0, 6); // Prevents Berserk/Manip
                                    data[o] = 0; o++;
                                    data[o] = 0; o++;
                                    data[o] = (byte)(status[picker]); o++;
                                    data[o] = 0; o++;
                                }
                                else
                                {
                                    picker = rnd.Next(2, 3); // Only Paralysis/Darkness available
                                    data[o] = 0; o++;
                                    data[o] = 0; o++;
                                    data[o] = 0; o++;
                                    data[o] = (byte)status[picker]; o++;
                                }

                            }
                            else
                            {
                                if (picker == 0)
                                {
                                    picker = rnd.Next(0, 8);
                                    data[o] = (byte)status[picker]; o++;
                                    data[o] = 0; o++;
                                    data[o] = 0; o++;
                                    data[o] = 0; o++;

                                }
                                else if (picker == 1)
                                {
                                    picker = rnd.Next(8);
                                    data[o] = 0; o++;
                                    data[o] = (byte)(status[picker]); o++;
                                    data[o] = 0; o++;
                                    data[o] = 0; o++;
                                }
                                else if (picker == 2)
                                {
                                    picker = rnd.Next(8);
                                    data[o] = 0; o++;
                                    data[o] = 0; o++;
                                    data[o] = (byte)status[picker]; o++;
                                    data[o] = 0; o++;
                                }
                                else
                                {
                                    picker = rnd.Next(8);
                                    data[o] = 0; o++;
                                    data[o] = 0; o++;
                                    data[o] = 0; o++;
                                    data[o] = (byte)status[picker]; o++;
                                }
                            }
                        }
                        else
                        {
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                        }

                        // Padding FF
                        data[o] = 255; o++;
                        data[o] = 255; o++;
                        data[o] = 255; o++;
                        data[o] = 255; o++;
                    }
                    else
                    {
                        // Retain enemy info
                        o += 184;
                    }
                    r++;
                }
                r = 0;

                //array = enemyIDs.Select(b => (byte)b).ToArray();
                //bw.BaseStream.Position = 0x00298;
                //bw.Write(array, 0, array.Length);
                //o = 0;
                #endregion

                #region Attack Data
                error = "Attack Data";
                while (r < 32)
                {
                    // If MP cost does not equal 65536 or Target flags are 0
                    if (data[o + 4] != 255 && data[o + 5] != 255)
                    {
                        if (options[40] != false)
                        {
                            // Attack %
                            data[o] = (byte)rnd.Next(50, 150); o++;

                            // Impact Effect ID - Must be FF if Attack Effect ID is not FF
                            //data[o] = rnd.Next(0, 256); o++;
                            data[o] = data[o]; o++;

                            // Target Hurt Action Index
                            // 00 = Standard
                            // 01 = Stunned
                            // 02 = Heavy
                            // 03 = Ejected
                            //data[o] = rnd.Next(0, 4); o++;
                            data[o] = data[o]; o++;

                            // Unknown
                            data[o] = 255; o++;

                            // Casting Cost
                            data[o] = (byte)rnd.Next(0, 256); o++;
                            data[o] = 0; o++;

                            // Impact Sound - Must be FFFF if Attack Effect ID is not FF
                            //data[o] = rnd.Next(0, 256); o++;
                            //data[o] = rnd.Next(0, 256); o++;
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;

                            // Camera Movement ID for single target - FFFF if none
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;

                            // Camera Movement ID for multi target - FFFF if none
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;

                            // Target Flags - Logic will be tough for this one; will depend on attack element + attack type as some aren't designed for multi-target
                            data[o] = data[o]; o++;
                            //data[o] = data[o]; o++;

                            // Attack Effect ID - Must be FF if Impact Effect is not FF
                            data[o] = data[o]; o++;
                            //data[o] = data[o]; o++;

                            // Damage Calculation
                            data[o] = data[o]; o++;

                            // Base Power
                            data[o] = (byte)rnd.Next(0, 40); o++;

                            // Condition Sub-Menu Flags
                            // 00 = Party HP
                            // 01 = Party MP
                            // 02 = Party Status
                            // Other = None
                            data[o] = data[o]; o++;

                            // Status Effect Change
                            // 00-3F = Chance to inflict/heal status (#/63)
                            // 40 = Remove Status
                            // 80 - Toggle Status
                            // This is changed later if Status Effects Safe/Unsafe are active
                            data[o] = data[o]; o++;

                            // Attack Additional Effects
                            //data[o] = (byte)rnd.Next(0, 256); o++;
                            data[o] = data[o]; o++;

                            // Additional Effects Modifier Value
                            //data[o] = (byte)rnd.Next(0, 256); o++;
                            data[o] = data[o]; o++;
                        }
                        else
                        {
                            o += 20;
                        }


                        // Produce an enum class that holds the specific values for each status, then pick one of those or more and 
                        // pipe it into the statuses/elements; true random here would be too much + death/imprisoned/petrify can creep in

                        // Statuses - TODO: Identify values that are OHKOs.
                        if (options[41] != false) // Safe - OHKOs/Disables not enabled
                        {
                            /* Statuses (by flag)
                             * 1 = Death
                             * 2 = Near-Death
                             * 4 = Sleep
                             * 8 = Poison
                             * 16 = Sadness
                             * 32 = Fury
                             * 64 = Confusion
                             * 128 = Silence

                             * 1 = Haste
                             * 2 = Slow
                             * 4 = Stop
                             * 8 = Frog
                             * 16 = Mini
                             * 32 = Slow-Numb
                             * 64 = Petrify
                             * 128 = Regen

                             * 1 = Barrier
                             * 2 = MBarrier
                             * 4 = Reflect
                             * 8 = Dual
                             * 16 = Shield
                             * 32 = D. Sentence
                             * 64 = Manip
                             * 128 = Berserk

                             * 1 = Peerless
                             * 2 = Paralysis
                             * 4 = Darkness
                             * 8 = Dual-Drain
                             * 16 = Death Force
                             * 32 = Resist
                             * 64 = Lucky Girl
                             * 128 = Imprisoned
                             */

                            //Random rndStatusSafe = new Random(seed);
                            int picker = rnd.Next(4);
                            int[] status = new int[] { 1, 2, 4, 8, 16, 32, 64, 128 };

                            // Sets status chance
                            data[o - 3] = (byte)rnd.Next(0, 64);

                            if (picker == 0)
                            {
                                picker = rnd.Next(2, 8); // Prevents Death and Near-Death being set
                                data[o] = (byte)status[picker]; o++;
                                data[o] = 0; o++;
                                data[o] = 0; o++;
                                data[o] = 0; o++;

                            }
                            else if (picker == 1)
                            {
                                picker = rnd.Next(2, 7); // Prevents Petrify and Regen being set
                                data[o] = 0; o++;
                                data[o] = (byte)status[picker]; o++;
                                data[o] = 0; o++;
                                data[o] = 0; o++;
                            }
                            else if (picker == 2)
                            {
                                data[o - 3] = 255;
                                o += 4;
                            }
                            else
                            {
                                picker = 4; // Only Darkness set
                                data[o] = 0; o++;
                                data[o] = 0; o++;
                                data[o] = 0; o++;
                                data[o] = (byte)status[picker]; o++;
                            }
                        }
                        else if (options[42] != false) // Unsafe - OHKOs/Disables enabled
                        {
                            //Random rndStatusUnsafe = new Random(seed);
                            int picker = rnd.Next(4);
                            int[] status = new int[] { 1, 2, 4, 8, 16, 32, 64, 128 };

                            // Sets status chance
                            data[o - 3] = (byte)rnd.Next(0, 64);

                            if (picker == 0)
                            {
                                picker = rnd.Next(0, 7);
                                data[o] = (byte)status[picker]; o++;
                                data[o] = 0; o++;
                                data[o] = 0; o++;
                                data[o] = 0; o++;
                            }
                            else if (picker == 1)
                            {
                                picker = rnd.Next(0, 6); // Prevents Regen being set
                                data[o] = 0; o++;
                                data[o] = (byte)status[picker]; o++;
                                data[o] = 0; o++;
                                data[o] = 0; o++;
                            }
                            else if (picker == 2)
                            {
                                picker = rnd.Next(1, 7); // Prevents Barrier being set
                                data[o] = 0; o++;
                                data[o] = 0; o++;
                                data[o] = (byte)status[picker]; o++;
                                data[o] = 0; o++;
                            }
                            else
                            {
                                picker = rnd.Next(1, 7); // Prevents Peerless being set
                                if (picker == 8 && data[o - 1] != 8) // Checks for Dual-Drain, sets Dual on previous byte
                                {
                                    data[o - 1] = 8;
                                }
                                data[o] = 0; o++;
                                data[o] = 0; o++;
                                data[o] = 0; o++;
                                data[o] = (byte)status[picker]; o++;
                            }
                        }
                        else
                        {
                            o += 4;
                        }

                        // Elements - TODO: restrict to 1 element
                        if (options[43] != false)
                        {
                            int picker = rnd.Next(2);
                            int[] element = new int[] { 1, 2, 4, 8, 16, 32, 64, 128 };

                            if (picker == 0)
                            {
                                picker = rnd.Next(0, 7);
                                data[o] = (byte)element[picker]; o++;
                                data[o] = 0; o++;
                            }
                            else if (picker == 1)
                            {
                                picker = rnd.Next(0, 7);
                                data[o] = 0; o++;
                                data[o] = (byte)element[picker]; o++;
                            }
                            else
                            {
                                o += 2;
                            }
                        }
                        else
                        {
                            data[o] = data[o]; o++;
                            data[o] = data[o]; o++;
                        }

                        // Special Attack Flags
                        //data[o] = (byte)rnd.Next(0, 256); o++;
                        //data[o] = (byte)rnd.Next(0, 256); o++;
                        data[o] = data[o]; o++;
                        data[o] = data[o]; o++;
                    }
                    else
                    {
                        while (c < 28)
                        {
                            data[o] = data[o]; o++;
                            c++;
                        }
                        c = 0;
                    }
                    r++;
                }
                r = 0;

                //array = attackData.Select(b => (byte)b).ToArray();
                //bw.BaseStream.Position = 0x004C0;
                //bw.Write(array, 0, array.Length);
                //o = 0;
                #endregion

                #region Attack IDs
                error = "Attack IDs";
                while (r < 32)
                {
                    // Attack ID - These should match the ones referenced in AI and Animation Attack IDs
                    //attackIDs[o] = rnd.Next(0, 256); o++;
                    data[o] = data[o]; o++;
                    data[o] = data[o]; o++;
                    r++;
                }
                r = 0;

                //array = attackIDs.Select(b => (byte)b).ToArray();
                //bw.BaseStream.Position = 0x00840;
                //bw.Write(array, 0, array.Length);
                //o = 0;
                #endregion

                #region Attack Names
                while (c < 32)
                {
                    // Attack Name, 32 bytes ascii
                    if (data[o] != 255 && options[44] != false)
                    {
                        nameBytes = AllMethods.NameGenerate(rnd);
                        data[o] = nameBytes[0]; o++;
                        data[o] = nameBytes[1]; o++;
                        data[o] = nameBytes[2]; o++;
                        data[o] = nameBytes[3]; o++;
                        rngID = rnd.Next(2); // Chance to append a longer name
                        if (rngID == 1)
                        {
                            data[o] = nameBytes[4]; o++;
                            data[o] = nameBytes[5]; o++;
                            data[o] = nameBytes[6]; o++;
                            data[o] = nameBytes[7]; o++;
                        }
                        else
                        {
                            data[o] = 0; o++;
                            data[o] = 0; o++;
                            data[o] = 0; o++;
                            data[o] = 0; o++;
                        }
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 0; o++;
                        data[o] = 255; o++; // Empty - Use FF to terminate the string
                    }
                    else
                    {
                        o += 32;
                    }
                    c++;
                }
                //array = attackNames.Select(b => (byte)b).ToArray();
                //bw.BaseStream.Position = 0x00880;
                //bw.Write(array, 0, array.Length);
                //o = 0;
                //c = 0;
                #endregion

                #region Formation AI Script Offsets
                error = "Formation AI Offsets";
                // These need to match the location of each one
                //data[o] = 0; o++;
                //data[o] = 0; o++;
                //data[o] = 0; o++;
                //data[o] = 0; o++;
                //data[o] = data[o]; o++;
                //data[o] = data[o]; o++;
                //data[o] = data[o]; o++;
                //data[o] = data[o]; o++;

                //array = formationAIOffset.Select(b => (byte)b).ToArray();
                //bw.BaseStream.Position = 0x000C80;
                //bw.Write(array, 0, array.Length);
                //o = 0;
                #endregion

                #region Formation AI
                error = "Formation AI";
                // This is likely best served from a notepad containing AI scripts, though formation AI itself is very rarely used (Final Sephiroth fight)
                //array = formationAI.Select(b => (byte)b).ToArray();
                //bw.BaseStream.Position = 0x000C88;
                //bw.Write(array, 0, array.Length);
                //o = 0;
                #endregion

                #region Enemy AI Offsets
                error = "Enemy AI Offsets";
                // These need to match the location of each one
                //if(options[45] != false){}
                //enemyAIOffset[o] = 0; o++;
                //enemyAIOffset[o] = 0; o++;
                //enemyAIOffset[o] = 0; o++;

                //array = enemyAIOffset.Select(b => (byte)b).ToArray();
                //bw.BaseStream.Position = 0x000E80;
                //bw.Write(array, 0, array.Length);
                //o = 0;
                #endregion

                #region Enemy AI
                error = "Enemy AI";
                // This is likely best served from a notepad containing AI scripts
                //array = formationAI.Select(b => (byte)b).ToArray();
                //bw.BaseStream.Position = 0x000E86;
                //bw.Write(array, 0, array.Length);
                #endregion
                //}
            }
            catch
            {
                MessageBox.Show("Scene ID: " + sceneID + " has failed to randomise; point of error: " + error);
            }
            return data;
        }
    }
}