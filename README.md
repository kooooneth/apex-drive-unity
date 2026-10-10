# Apex Drive - Unity edition

A port of the Three.js "Apex Drive" first-person F1 game to Unity. Everything (track meshes, cars, HUD,
menu, weather) is generated in code, so there is nothing to wire up in the editor.

## Run it
1. Install Unity **2022.3 LTS** (newer versions open it too and will offer to upgrade).
2. Unity Hub -> Add -> select this `apex-drive-unity` folder -> open it. Use the built-in render
   pipeline (this project has no URP/HDRP package; if you drop the scripts into a URP project the
   materials fall back to the URP Lit shader automatically).
3. Open any scene (or leave the default empty one) and press **Play**. `Bootstrap` creates the game
   at startup via `RuntimeInitializeOnLoadMethod`.
4. If your project's Active Input Handling is "Input System Package (New)", switch it to
   "Input Manager (Old)" or "Both" under Project Settings > Player (the scripts use `UnityEngine.Input`).

## Controls
W / Up throttle, S / Down brake, A D / Left Right steer, Q DRS (in the DRS zone), E engine mode,
V cockpit / chase camera, R recover to centre line, P or Esc pause, 1 - 5 choose tires during a pit stop (soft, medium, hard, inter, wet), Y / N answer the pit-wall radio calls.
B = box this lap (lane assist + stops you in the box), Shift = ERS overtake button (spends the battery), Z / X / C = look left / right / behind.
Replay (from the results screen): Space pause, Left/Right scrub, 1 drone, 2 trackside, 3 chase, 4 next car, F hide UI, Enter photo, Esc exit.

## What's ported
- The three circuits (Harborline, Desert Straight, Sprint Alley; Silverstone was removed from the Unity version) with the same control points,
  elevation profiles and per-track rain / fog odds; same Catmull-Rom spline, arc-length spacing, wall
  clearance and curvature maths (`TrackModel.cs`).
- Road, curbs, guard rails, gravel, terrain skirt, grandstands, trees, hills, start/finish gantry.
- Pit lane as a real separate road, with its own wall between lane and track, pit crew, sign and box
  marking. Pit stops are opt-in: you must steer into the lane (never a forced magnet).
- Car physics from `updatePlayer()`: drag-limited acceleration, tire wear + compounds, wing / gear / brake
  setup, corner limit, crest launch + gravity, wall damage, track limits + penalties, slipstream, DRS.
- AI rivals with look-ahead braking, mistakes, tire wear and pit stops.
- Weather (rain with wet-track grip loss, fog), sector times with deltas vs your best, lap timing, HUD,
  minimap, garage with parts shop / tires / setup, results + prize money. Progress saves via PlayerPrefs.

## Also ported (second batch)
Career season (3 rounds, F1 points), tournament (unlocks at driver level 5, tougher AI, $5000 prize),
driver XP + levels (steering aid grows up to +15%), 7 achievements, livery editor (colors + solid / stripe /
checkered), driver choice, qualifying (sets your grid slot per circuit), best-lap ghost car, safety car
(30% chance per race), AI difficulty, and procedural audio (engine, tire screech, impacts, crowd cheer).

## Not ported
Dedicated cockpit gauges / radio chatter variety. Everything else from the web game is in.

## Notes
- Coordinates: the game logic runs in the HTML game's coordinate space and `TrackModel.W()` converts to
  Unity world space (z is negated because Three.js is right-handed and Unity is left-handed) so every
  circuit looks identical to the web version.
- The off-track limit is `ROAD_WIDTH / 2 + 0.7`. The HTML build had a stale `8.2` from the old narrower
  road; that is fixed there too.

## Race director update (fourth batch)
- **Qualifying knockout**: Q1 (6 cars, slowest 2 out), Q2 (4 cars, slowest 1 out), Q3 pole shootout. Rival lap times come from their pace ratings; your final classification sets your grid slot. New "Pole Position" achievement.
- **Weather forecast** on the Race tab (decided before the start); rain can arrive or leave part-way through the race.
- **Intermediate and full-wet tyres** (keys 4 / 5 in the pit). Slicks lose lots of grip when wet, wets wear out quickly on a dry road. AI drivers switch tyres too.
- **Stewards**: collisions you cause, ignoring blue flags and overtaking under yellow give a warning, then +5 / +10 s penalties that count in the final classification.
- **Blue flags and lapping**: cars a lap ahead get a blue flag; AI drivers move off the line to let you by when you lap them.
- Optional **rolling starts** (Race tab -> Start).
- **Kerbs** shake the cockpit, scrub speed and twitch the car (much worse in the wet); hammering them at speed damages the floor and wears the tyres.
- **Track temperature** (shown in the forecast and on the HUD) shifts the tyre temperature windows: hot tracks cook softs, cold tracks leave hards slow to warm.
- AI pit stops now go through the real pit lane and stop in the box; the player's pit-stop detection uses the car's real position.
- **Virtual safety car** (speed delta of 137 km/h) alongside the safety car, and **red flags** with free tyres / repairs and a standing restart.
- **Team radio calls** with Y / N answers: rain coming, puncture, aero damage, safety-car stop, undercut cover, push for 25 s.
- **Damage model**: front wing, floor (top speed), engine (power), body, and slow punctures, with a small damage diagram on the HUD. Pit stops take longer for repairs.
- **Pit crew mistakes**: slow wheel gun, slow jack, or an unsafe release (+5 s).
- **Post-race telemetry**: compare any two laps - speed, throttle, brake and the time gap round the lap.

## Racer, endurance and custom seasons (fifth batch)
- **Your own racer** (Racer tab): choose a name (shown in races, results and standings) and train three stats up to level 10: *Fitness* (the driver tires more slowly in long races), *Focus* (less tyre wear, steadier over kerbs) and *Reflexes* (the car answers steering faster).
- **Gym exercises** (cost 1 energy each): Cardio sprint (mash Space), Reaction lights (hit Space when the lights go out), Focus drill (repeat the arrow sequence). Races refill 2 energy, practice sessions 1.
- **Practice tracks**: free solo sessions on the selected circuit - Smooth laps (Focus), Fast laps (Reflexes), Long run (Fitness) - with clean-lap and target-time scoring, a little cash and driver XP. Races also train your stats.
- **Endurance race** (6 / 10 / 15 laps): runs from afternoon into night (sky, sun and headlights follow the race), fuel burns with distance (F picks how much to take at a stop), the driver tires over time (kerbs and rain make it worse, Fitness helps) with a relief-driver swap at a stop, and tyre/fuel planning is shown in the menu. Pays double; AI cars make fuel stops too.
- **Custom season builder**: choose up to 6 rounds (any circuits, repeats allowed), laps per round, weather odds (dry / mixed / wet) and AI level, name it, and it saves automatically. Points, standings and a champion bonus like the other seasons.

## Pit boxes, race weekend and realism (sixth batch)
- **Six pit boxes** in a pit lane that runs parallel to the track on the outside of the bend (fast lane for passing cars, working lane by the garages). The pit assigns you box 3; every box has its own coloured sign and crew, and rival teams stop in their own boxes. The pit lane is placed on the straightest stretch of the lap and the outer wall is set back around it. (Harborline's pit zone moved because its old spot was in a tight bend.)
- **Setup sheet** (Garage): ride height, camber, differential and tyre pressure join wing / gear / brake bias. Each circuit has hidden ideal values; practice sessions give engineer feedback.
- **Race weekend** (Race tab): Practice 1-3, Qualifying (Q1-Q3), Race, with parc ferme locking the setup from qualifying on, and a bonus for finishing the weekend.
- **Weight transfer**: the body dives under braking, squats under power and rolls in corners, for your car and the rivals (and the cockpit camera).
- **Wet weather**: spray behind every car and standing-water puddles that cause aquaplaning (less on inters / wets).
- **Sun glare**: the sun's height and direction are part of the forecast; a low sun straight ahead dazzles you.
- **Rival trouble**: mechanical failures (smoking car, safety car or VSC, recovery truck), spins, lock-ups, stops under a caution, and a DNF in the results.
- **Crash physics**: a wheel-to-wheel touch can launch a car, wheels can be torn off (and roll away), parts break up.
- **Sound**: rival engines with Doppler, turbo whine and backfire pops, kerb rumble, gravel and wet-road sounds, echo near the grandstands, radio static.

## Team career, strategy and voices (seventh batch)
- **Team tab**: you start with Backmarker Racing; the car's quality comes from your team. After a career season good results bring offers from better teams (salary, contract length, an objective with a bonus). The two rival teams develop their cars every season.
- **Teammate**: AI 0 is your teammate in your livery. The pit wall can ask you to let him through (Y / N); it changes your team rapport.
- **R&D tree**: three branches (aero, engine, tyre management) bought with R&D points earned from races and clean practice laps.
- **Engine and gearbox wear**: every race and session wears them; worn parts lose power and can fail. Two replacements a season are free of penalty, more cost 3 grid places.
- **Racer appearance**: race number, helmet colour and nationality (shown on the car, in results and in the grid walk).
- **Training calendar**: races take a week; more than 10 days without training and your Fitness fades. A rest day gives +1 energy.
- **Wind tunnel and simulator** (Garage): the wind tunnel reveals the ideal value of one setup setting per circuit, the simulator gives instant setup feedback and an estimated lap.
- **Formation lap**: the Start option 'Formation' gives a grid-walk cutscene, then a full formation lap before the green flag.
- **Pit-wall strategy screen** (T): timing tower with tyres, stint age, stops, gaps and when each rival will pit, plus a rejoin predictor for 'pit now'.
- **Spoken radio and replay commentary**: the engineer and the pit crew speak over the radio during the race; the two commentators speak only in the replay (their lines are written down during the race: overtakes, spins, stops, cautions, the finish). Voices are Piper neural text-to-speech (offline, human-sounding): a British play-by-play commentator, an American race engineer (with a radio filter) and a woman adding colour. The Piper program and three voice files live in `%LOCALAPPDATA%\ApexDrive\piper` (not in the project); if they are missing the game falls back to the English Windows voices. It can be switched off in the Profile tab and is silent on other operating systems.

## Car physics, senses and the driver (eighth batch)
- **Four tyres**: each has a surface and a core temperature and its own load. Corners load the outside tyres, braking the fronts, power the rears. Each tyre heats and cools on its own, the cockpit wheel shows the four surface temperatures (blue cold, green in the window, red cooking), and an overheated surface blisters (more wear, less grip).
- **Aero platform**: ride height changes with speed (downforce squashes the car), braking, fuel load and kerbs. There is a sweet spot; too high or too low loses downforce, a very low car bottoms out (floor damage) and at speed porpoises.
- **Weight transfer** (Profile toggle): the front gives up first on power (understeer), the rear on trail-braking or lifting in a quick corner (a slide you catch by steering against it). The engineer comments.
- **Fuel burn**: in sprint races the car gets lighter every lap (sprint effects are centred so the average lap is unchanged); endurance keeps the full effect. The engineer reports what it is worth.
- **ERS maps** (M): HARVEST, BALANCED, ATTACK change how the battery is charged and spent on the straights; the SHIFT overtake button still adds the full boost.
- **Gearbox**: eight gears with a real rev counter, torque cut at each shift, engine braking and a bouncing rev limiter at top speed. Optional manual mode (G, or the Profile tab; . up, , down): over-revving a downshift stresses the engine and locks the rears, staying in too high a gear bogs the car.
- **Driver's head** moves with the g-forces (outwards in corners, forward under braking) and the cockpit vibrates with speed.
- **Spotter**: "car left / car right / clear / three wide" calls for cars in your blind spot (Profile toggle; hold Z or X to look).
- **Sound**: the engine follows the gearbox, gearshift clunks, wind round the helmet, brake squeal, rain on the visor and a crowd murmur near grandstands.
- **Visor** (H tears off a strip): rain droplets collect and the wind clears them, it mists up when slow in the wet or fog, and the low sun lingers in your eyes (and flickers through trees).

## Circuits, crew, night and damage (ninth batch)
- **Six circuits, six cars**: Marina Streets (walls right beside the road), Alpine Pass (big climbs) and Grand Ring (long and fast) join the first three; every circuit has its own car (setup, gearing, livery, tyres) and the Garage tab switches between them. Career and tournament still use the original three rounds; the new circuits are in Quick Race and custom seasons.
- **Night races**: Race tab > Time (Day / Dusk / Night): floodlight poles with lamp heads, soft pools of light on the road, real lights that follow the car, stars, and headlights on every car.
- **Visible damage**: mirrors, front-wing end plates and bargeboards can be knocked off by walls and contact (the glass of a lost mirror disappears in the cockpit). Lost end plates and boards cost downforce; a pit stop puts everything back.
- **Safety car restarts**: the field now bunches up behind the safety car; it comes in a lap later and the leader sets the pace until the control line (no overtaking). Brake-testing the field for too long earns a penalty.
- **Better rain**: puddles build up through a long wet spell (more and bigger ones appear, aquaplaning gets worse), a drying line shows as lighter dry asphalt on the wet road, much thicker spray trails, and a spray haze when you follow a car closely.
- **Regulation eras** (every two career seasons): ground-effect cars, hybrid boost, heavy cars, spec tyres or an engine freeze. The best setup values move and the wind tunnel's findings are forgotten, so setups need re-tuning. Shown in the Team tab.
- **Crew tab**: sponsors (choose one; meet its goal in a race for money; deals grow and make the parts shop cheaper) and staff (race engineer, chief mechanic, aerodynamicist, levels 0 to 3).
- **Wind tunnel and simulator**: now a real lap simulation of your car on the circuit (the same top-speed, acceleration, braking, steering-rate, tyre-grip, aero-platform and tyre-wear formulas as the car physics). The ideal ride height, wing and gear come from searching it for the quickest four-lap stint; camber, differential and pressure follow the circuit's character (corner share, slow corners, rain odds). Six settings can be measured, each reading says what moving there is worth in seconds per lap, and readings are only valid for the car as it is (upgrades, team or rules change them). The simulator gives a simulated clean lap and the gain of the best possible setup. Tyre grip now limits corner speed as v = sqrt(a x R) in the handling model, the same rule the simulation uses.
- **Leaderboard** (top right of the race screen, L to hide): live positions with the gap to the leader and each car's tyre; you always stay on the board.
- **Tyre strategy planner** (Race tab): choose 0 to 2 stops, the compound of each stint and the lap to box on; the predicted race time, each stint's average lap and the tyre wear at its end come from the lap simulation. "Find the fastest plan" searches every plan. When you follow it, the race starts on the first tyre and the engineer asks you to box on the planned laps.
- **Brake ducts and brake wear**: brake ducts are part of each car's setup (large ducts cool the brakes but add a little drag), brakes wear with use (hot brakes wear faster, worn brakes bite less) and a new set costs $400 in the Garage tab.
- **Road surface details**: patches of old and new tarmac (a little less or more grip), drain covers at the road edges (slippery in the wet, they twitch the car) and low bumps that shake the car and squash its floor. They are drawn on the road and felt by the car.
- **Camera views** (V cycles): cockpit, halo cam (on top of the halo looking down the front of the car), helmet cam (a wide view from the driver's eyes with a visor frame, rain and head motion), chase and a TV broadcast director (trackside, helicopter and low cameras cutting every few seconds). Every change of view blends smoothly.
- **Rewind** (Backspace): three rewinds per race undo a mistake. You and the cars around you go back about six seconds, the damage is undone and the undone time does not count against the race clock. It can be switched off in the Profile tab; it is off in the tutorial and in practice.
- **Practice challenges** (Racer tab) with bronze, silver and gold medals for each circuit: a perfect lap (clean and quick, times from the lap simulation), a braking test (reach a speed, then stop with the nose on the white line) and tyre saver (three laps inside a time limit on as little tyre wear as possible). Medals pay cash, XP and R&D points.
- **Commentary** speaks about every two seconds in the replay, with 40 short filler lines.
- **Radio that talks less (but not too little)**: every message has a priority. Chatter (gaps, lap times, tips) is only said after a long silence and dropped otherwise; information waits for a quiet moment; warnings and answers to what you just did come at once.
- **A more real engineer**: he names the cars around you with their tyre and tyre age, reports the gap and whether you are catching him, calls rival stops, names the hottest tyre corner and its temperature, and warns about wind, glare and fuel. He remembers whether you follow his calls: ignore one and he asks again, then trusts you less and goes quiet (shown on the results screen).
- **Fuel load**: the tank is real in every race (endurance still refuels). The car gets lighter as it burns off, so braking, grip, tyre wear and ride height change lap by lap. Push mode burns more, save mode and lift-and-coast burn less; the HUD shows the margin in laps and the engineer offers a fuel-save when you are short.
- **Tyre graining and blistering**: a cold surface that is worked hard grains (hards most), a cooked one blisters (softs most). Each corner has its own damage, which costs grip and clears (graining) or heals very slowly (blisters). New tyres start clean.
- **Real DRS**: zones are found on each circuit's flattest stretches, with a detection point before each one and boards and a white line trackside. Cross the detection line within 1 second of the car ahead to be allowed to open the rear wing (hold Q) in the zone; braking shuts it until the zone ends. Not in the wet, behind a safety car or in the first 15 seconds. The flap really opens on every car, AI cars use it too, and other players see yours.
- **Wind**: each race has a wind with gusts. A headwind lowers top speed and adds grip in corners, a tailwind the reverse, and a crosswind pushes the car sideways. A gauge under the radar shows it, rain is blown sideways, and the grandstand flags wave harder.
- **Real sun**: each circuit has a latitude and each race a date and start time, so the sun's height and direction are real, shown in the forecast and creeping across the sky during the race (shadows lengthen and turn). Glare adds lens-flare ghosts and is worse through a dirty or misted visor.
- **Online multiplayer with a room code** (Race tab, ONLINE MULTIPLAYER): up to 6 people. The host presses CREATE ROOM and shares the 8-letter code (like `2CWA-CF2H`, it is the host's address); everybody else types it under JOIN ROOM. The host picks circuit, laps, time of day and start type, then START RACE. Everybody gets the same weather, drives their own car and setup, and sees the others on the grid, in the leaderboard and in the pits. Cars can touch. No random safety cars, rewind or pause online (Esc opens a menu while the race keeps running). No server or account: it is direct UDP. Same Wi-Fi or network works as is; over the internet the host types their public IP in 'Your address' and forwards UDP ports 47777-47784. The first time you host, Windows asks to allow the game through the firewall.
- **Reliability**: the engine and gearbox heat up (push mode, hot track and worn parts make it worse). Warnings come first ("lift and coast"); ignore them and the engine blows or the gearbox sticks in gear. A pit stop cools and resets them. Overheats, failures and big impacts feed **test-track data** that unlocks fixes (3 levels each: engine cooling, gearbox cooling, impact structure).
- **Wet line choice**: on a wet road the racing line dries first and grips better, off it stays greasy, and in a long wet spell the puddles creep sideways across the road.
- **Facilities** (Crew tab): factory (+R&D, cheaper parts), wind tunnel (more settings per run) and simulator (cheaper runs, shows what each setting is worth).
- **Broadcast graphics**: TV banners for overtakes, lead changes, pit in/out (with the tyre change), the final lap and every lap time with the delta to your best. A **weather radar** (top left) shows the rain cells and a countdown to the weather change. Dusk adds a sunset glow at the horizon and the floodlights come on gradually.

## Realism systems (third batch)
Tyre temperature windows + slip (understeer / trail-brake oversteer), brake temperature and fade, ERS battery with an overtake button, dirty air behind other cars, front-wing damage with falling debris, look left/right/behind, working mirrors, replay with a drone camera and photo mode, marshal flags (green / yellow / blue / white), a rubbered-in racing line with a drier line in the rain, animated crowd with flags and fireworks for a win, AI personalities (aggressive, cautious, tyre saver, wet specialist) and a race engineer on the radio.

## Voices (Piper)
The spoken radio uses Piper text-to-speech, which is not in this repository (about 440 MB). Put the Piper program in `PiperVoices/piper/` and the voice files (`en_GB-alan-medium`, `en_US-ryan-high`, `en_GB-cori-high`, with their `.onnx.json`) in `PiperVoices/voices/`, from https://github.com/rhasspy/piper. Without them the game falls back to the English Windows voices.

## Download (Windows)
Get the app: https://github.com/kooooneth/apex-drive-unity/releases/latest/download/ApexDrive-Windows.zip (27 MB). Unzip it and run `ApexDrive.exe`. Windows may warn about an unknown publisher: More info, then Run anyway. To rebuild it, use the menu Apex Drive > Build Windows (or `-executeMethod ApexDrive.Editor.WebGLBuilder.BuildWindows -buildTarget Win64`), zip the `Build/Windows` folder and attach it to a new release.

## Play in the browser
The full game runs as WebGL at https://kooooneth.github.io/apex-drive-unity/unity/ (computer and keyboard, recent Chrome, Edge or Firefox). The browser build adjusts its shadows to your graphics card; the voices use the browser's speech synthesis; online multiplayer is desktop only.

To rebuild it after changing the scripts, close the project in the editor, then run (adjust the Unity path):

    Unity.exe -batchmode -quit -projectPath . -buildTarget WebGL -executeMethod ApexDrive.Editor.WebGLBuilder.Build

or use the menu Apex Drive > Build WebGL. The build lands in `Build/WebGL`; copy its contents to `docs/unity` and push. (`Assets/Editor/WebGLBuilder.cs` sets the options; `Assets/WebGLTemplates/Apex` is the page.) Primitives are made by `PrimitiveMeshes` so the browser build needs no physics colliders.
