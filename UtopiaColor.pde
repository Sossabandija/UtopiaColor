import com.jogamp.newt.opengl.GLWindow;
import processing.sound.*;
import com.hamoid.*;

String VIDEO_FILENAME = "piece_carnie.mp4";
boolean SAVE_VIDEO = true;
VideoExport videoExport;

SoundFile[] sfx;
String[] sfx_names = {"splash0", "splash1", "splash2", "splash3", "splash4", "jump", "land",
  "eat1", "eat2", "eat3", "eat4", "eat5", "seed", "baby", "oof"};

int PLAYER_COUNT = 110;
int DIM_COUNT = 3;
int CENTER_X = 760; // try setting this to 760 or 761 if there is horizontal camera-pan-drifting
color SKY_COLOR = color(150, 200, 255);
KeyHandler keyHandler;
float TICKS_PER_DAY = 4500;
boolean TRAP_MOUSE = true;

int SIZE = 36;
int T = 140; // T = tile size
float COLLISION_DISTANCE = 33; // how close to creatures have to be to interact?
int MAX_PER_TILE = 2;
float VISION_DISTANCE = T*8.6;
color[] SPECIES_COLORS_UI = {color(0, 200, 0), color(60, 128, 255), color(255, 0, 0)};
float PLANT_MUTATION = 0.62; // 0.62
float ANIMAL_MUTATION = 0.46; // 0.42


float EPS = 0.001;
PGraphics g;
PGraphics ui;
PGraphics wheel;
PGraphics colorWheel;
int frames = 0;
int ticks = 0;
int playback_speed = 1;
int FPS = 30;
GLWindow r;
Map map;
PImage ui_title;
int MAX_ID = 0;

float DISTANCE_FROM_PLAYER = 200;
ArrayList<Record> archive;
int ARCHIVE_EVERY = 30;
int ARCHIVE_SIZE = 200;
int SPECIES_COUNT = 3;
int[] START_SPECIES = {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2};
color WATER_COLOR = color(40, 80, 220);
ArrayList<Player> players;
ArrayList<Gut> guts;
boolean followSpecimen = false;

String[] PRIORITY_NAMES = {"Hunger", "Thirst", "Freaky", "Eepy", "Flee Monsters", "Caretaking"};
color[] PRIORITY_COLORS = {color(150, 100, 50), color(0, 0, 255), color(255, 0, 255), color(128, 255, 0), color(255, 0, 0), color(255, 188, 120)};
float[] PRIORITY_CAPS = {0.0, 0.0, 0.6, 0.6, 0.0, 0.0};

// how quickly does each species gain urgency in doing the fundamental tasks?
// Each row is per species
// Each element in row is per priority
float[][] PRIORITY_RATES = {
  {3.0, 0, 0, 0, 0, 0}, // Flower
  {18.0, 3.3, 0.8*5.4, 0, 0, -60}, // Cow
  {16.5, 5.0, 0.8*5.4, 0, 0, -60}}; // red predator

boolean[][] IS_FOOD = { // can each species eat the others?
  {false, false, false},
  {true, false, false},
  {false, true, false}};

float[] CALORIES_RATE = {1.35, 1.45, 0.80};
// what multiplier of calories does consuming each animal give? (multiplied by hunger level)

float WATER_CALORIES = 0.40;
float[] SPECIES_SPEED = {0.0, 0.51, 0.58};
int ACTION_COUNT = 8; // jump, WASD, turn left, turn right, wander

Player closest_AI;
float[] camera = {0, 0, 0, 0, 0};
int TICK_BUCKET_COUNT = 20;
void setup() {
  map = new Map(SIZE);
  archive = new ArrayList<Record>(0);
  guts = new ArrayList<Gut>(0);

  players = new ArrayList<Player>(0);
  for (int i = 0; i < PLAYER_COUNT; i++) {
    float dx = random(0, SIZE*T);
    float dy = random(0, SIZE*T);
    float[] coor = {dx, dy, -10, random(0, 2*PI)};
    int species = -1;
    if (i >= 1) {
      int index = (int)(((float)i)/PLAYER_COUNT*START_SPECIES.length);
      species = START_SPECIES[index];
    }
    float[] dna = {0.7,0.5};
    Player newPlayer = new Player(species, coor, false, true, random(0.3333, 0.6666), random(0.3333, 0.6666), 0, "PRIMORDIAL", mutate(dna, 0.1, 1.0));
    players.add(newPlayer);
  }

  keyHandler = new KeyHandler();
  size(1920, 1080, P3D);

  r = (GLWindow)surface.getNative();
  if (TRAP_MOUSE) {
    r.confinePointer(true);
    r.setPointerVisible(false);
  }
  g = createGraphics(1520, 1080, P3D);
  ui = createGraphics(400, 1080, P2D);
  wheel = createWheel(400);
  colorWheel = createGraphics(400,400,P2D);
  drawColorWheel();
  clearUI();

  sfx = new SoundFile[sfx_names.length];
  for (int i = 0; i < sfx_names.length; i++) {
    sfx[i] = new SoundFile(this, sfx_names[i]+".wav");
  }
  sfx[10].play();
  frameRate(FPS);
  ellipseMode(RADIUS);
  ui_title = loadImage("title.png");
  
  if(SAVE_VIDEO){
    videoExport = new VideoExport(this, VIDEO_FILENAME);
    videoExport.setFrameRate(FPS);
    videoExport.startMovie();
  }
}
void draw() {
  doMouse();
  for (int i = 0; i < map.renderSpeed; i++) {
    doPhysics();
    doArchive();
    doGarbageRemoval();
  }

  drawVisuals();
  drawUI();
  background(255, 0, 255);

  image(g, 0, 0);
  image(ui, width-ui.width, 0);
  drawHUD();
  if(SAVE_VIDEO){
    videoExport.saveFrame();
  }
  frames++;
}

PGraphics createWheel(int diam){
  PGraphics result = createGraphics(diam, diam);
  result.beginDraw();
  result.background(0);
  
  float rad = diam/2.0;
  for(int px = 0; px < diam; px++){
    float x = (px-rad)/rad;
    for(int py = 0; py < diam; py++){
      float y = (py-rad)/rad;
      float dist_ = dist(0,0,x,y);
      if(dist_ > 1.0){
        continue;
      }
      float ang = atan3(y,x)/(2*PI);
      float[] dna = {ang, dist_};
      color col = dnaToColor(dna);
      result.set(px,py,col);
    }
  }
  result.endDraw();
  return result;
}
float atan3(float y, float x){
  float result = atan2(y,x);
  while(result < 0){
    result += 2*PI;
  }
  return result;
}
void drawHUD() {
  drawRenderSpeed();
  drawCrossHairs();
}
void drawRenderSpeed(){
  float timeOfLast = max(keyHandler.keysPressTime[11], keyHandler.keysPressTime[12]);
  if(millis()-timeOfLast > 2000){
    return;
  }
  textAlign(CENTER);
  textSize(64);
  fill(255);
  text(map.renderSpeed+"x speed", g.width/2, g.height/4);
}
void drawCrossHairs(){
  if(!TRAP_MOUSE) {
    return;
  }
  noStroke();
  fill(255);
  float W = 2;
  float L = 20;
  rect(g.width/2-W, g.height/2-L, W*2, L*2);
  rect(g.width/2-L, g.height/2-W, L*2, W*2);
}
int[] getPopulations() {
  int[] result = new int[SPECIES_COUNT];
  for (int s = 0; s < SPECIES_COUNT; s++) {
    result[s] = 0;
  }
  for (int i = 0; i < players.size(); i++) {
    int s = players.get(i).species;
    if (s >= 0) {
      result[s]++;
    }
  }
  return result;
}

void doArchive() {
  if (ticks%ARCHIVE_EVERY != 0) {
    return;
  }
  Record newRecord = new Record(getPopulations(), daylight());
  archive.add(newRecord);
  while (archive.size() > ARCHIVE_SIZE) {
    archive.remove(0);
  }
  drawColorWheel();
}
void drawColorWheel(){
  colorWheel.beginDraw();
  colorWheel.image(wheel,0,0);
  float rad = wheel.height/2.0;
  for(int type = 0; type < SPECIES_COUNT; type++){
    for (int i = 0; i < players.size(); i++) {
      Player p = players.get(i);
      if(p.species != type){
        continue;
      }
      float px = rad+rad*cos(p.dna[0]*(2*PI))*p.dna[1];
      float py = rad+rad*sin(p.dna[0]*(2*PI))*p.dna[1];
      colorWheel.push();
      colorWheel.translate(px,py);
      drawCWsymbol(colorWheel, p.species);
      colorWheel.pop();
    }
  }
  colorWheel.endDraw();
  wheel.save("colorWheel.png");
}
void drawCWsymbol(PGraphics img, int s){
  img.noFill();
  img.strokeWeight(1);
  img.stroke(darken(SPECIES_COLORS_UI[s],0.7));
  if(s == 0){
    img.line(-5,-5,5,5);
    img.line(-5,5,5,-5);
  }else if(s == 1){
    img.rect(-5,-5,10,10);
  }else if(s == 2){
    img.ellipseMode(RADIUS);
    img.ellipse(0,0,5,5);
  }
}
String getNewName() {
  int len = (int)random(4.8, 8);
  String[] lets = {"BCDFGHJKLMNPQRSTVWXZ", "AEIOUY"};
  String result = "";
  boolean inversion = (random(1) < 0.5);

  for (int L = 0; L < len; L++) {
    String options = lets[L%2];
    if (inversion) {
      options = lets[1-L%2];
    }
    char let = options.charAt((int)random(options.length()));
    if (L >= 1) {
      let = Character.toLowerCase(let);
    }
    result += let;
  }
  return result;
}
void doGarbageRemoval() {
  for (int i = players.size()-1; i >= 0; i--) {
    Player p = players.get(i);
    if (p.toDie) {
      Gut gut = new Gut(p.coor, ticks, p.species, p.dna);
      guts.add(gut);
      players.remove(i);
    }
  }
  for (int i = guts.size()-1; i >= 0; i--) {
    if (guts.get(i).toDie()) {
      guts.remove(i);
    }
  }
}
double[] getArchiveRange() {
  double[] result = {0, -999999999};
  for (int i = 0; i < archive.size(); i++) {
    for (int s = 0; s < SPECIES_COUNT; s++) {
      int val = archive.get(i).populations[s];
      if (val < result[0]) {
        result[0] = val;
      }
      if (val > result[1]) {
        result[1] = val;
      }
    }
  }
  return result;
}

void drawUI() {
  double[] range = getArchiveRange();
  ui.beginDraw();
  if (closest_AI == null || !followSpecimen) {
    ui.image(colorWheel,0,ui_title.height);
  }else{
    closest_AI.trait.drawDisplay();
    ui.image(closest_AI.trait.display, 0, ui_title.height);
  }
  
  ui.strokeWeight(3);
  ui.textAlign(LEFT);

  if (archive.size() == 0) {
    return;
  }
  Record archiveNow = archive.get(archive.size()-1);
  int[] species_order = new int[SPECIES_COUNT];
  boolean[] taken = new boolean[SPECIES_COUNT];
  for (int s = 0; s < SPECIES_COUNT; s++) {
    taken[s] = false;
  }
  for (int spot = 0; spot < SPECIES_COUNT; spot++) {
    int record = -1;
    int recordHolder = -1;
    for (int s = 0; s < SPECIES_COUNT; s++) {
      if (taken[s]) {
        continue;
      }
      if (archiveNow.populations[s] > record) {
        record = archiveNow.populations[s];
        recordHolder = s;
      }
    }
    taken[recordHolder] = true;
    species_order[spot] = recordHolder;
  }
  
  float W = 340.0;
  float y_bottom = DL_to_Y(0);
  ui.noStroke();
  ui.fill(70);
  ui.rect(0,y_bottom,W,ui.height-y_bottom);

  float textY = 100000;
  for (int i = 0; i < archive.size()-1; i++) {
    float x1 = i*W/(archive.size()-1);
    float x2 = (i+1)*W/(archive.size()-1);
    float y1 = DL_to_Y(archive.get(i).daylight);
    float y2 = DL_to_Y(archive.get(i+1).daylight);
    ui.stroke(128);
    ui.line(x1, y1, x2, y2);

    for (int spot = SPECIES_COUNT-1; spot >= 0; spot--) {
      int s = species_order[spot];
      int val1 = archive.get(i).populations[s];
      int val2 = archive.get(i+1).populations[s];
      y1 = val_to_Y(val1, range);
      y2 = val_to_Y(val2, range);
      ui.stroke(SPECIES_COLORS_UI[s]);
      ui.line(x1, y1, x2, y2);
      if (i == archive.size()-2) {
        float FS = 38;
        if(val2 >= 1000){
          FS = 25;
        }else if(val2 >= 100){
          FS = 33;
        }
        ui.fill(SPECIES_COLORS_UI[s]);
        ui.textSize(FS);
        textY = min(textY-FS+1, y2+6);
        ui.text(""+val2, x2+4, textY);
      }
    }
  }
  ui.image(ui_title, 0, 0);
  ui.fill(255);
  ui.textAlign(LEFT);
  ui.textSize(40);
  ui.text(nowToDateString(), 10, ui.height-10);
  ui.endDraw();
}

String nowToDateString() {
  float days = ticks/TICKS_PER_DAY;

  int hour = (int)((days%1)*24);
  int minute = (int)((((days%1)*24)%1)*60);
  String hourPiece = (hour%12 == 0 ? "12" : (hour%12)+"");
  String hourString = hourPiece+":"+nf(minute, 2, 0)+" "+((hour >= 12) ? "PM" : "AM");
  return "Day "+(int)(days+1)+" at "+hourString;
}

float daylight() {
  return 0.5-0.5*cos(ticksToDays(ticks)*(2*PI));
}

float DL_to_Y(double DL) {
  return (float)(1020-200*DL);
}
float val_to_Y(double elev, double[] range) {
  double ratio = (elev-range[0])/(range[1]-range[0]);
  return (float)(1020-200*ratio);
}
color colorLerp(color a, color b, float prog) {
  float newR = red(a)+prog*(red(b)-red(a));
  float newG = green(a)+prog*(green(b)-green(a));
  float newB = blue(a)+prog*(blue(b)-blue(a));
  return color(newR, newG, newB);
}

color dnaToColor_3D(float[] dna) {
  color result = dnaToColor(dna);
  return darken(result,0.9);
}
color dnaToColor(float[] dna) {
  float hue = dna[0];
  float sat = dna[1];

  float h = hue*6.0;
  float h_rem = (h%1.0);
  
  float mid_down = 255*(1-sat*h_rem);
  float mid_up = 255*(1-sat+sat*h_rem);
  float low = 255*(1-sat);
  if (h < 1.0) {
    return color(255, mid_up, low);
  } else if (h < 2.0) {
    return color(mid_down, 255, low);
  } else if (h < 3.0) {
    return color(low, 255, mid_up);
  } else if (h < 4.0) {
    return color(low, mid_down, 255);
  } else if (h < 5.0) {
    return color(mid_up, low, 255);
  } else {
    return color(255, low, mid_down);
  }
}
float[] mutate(float[] dna, float amount, float exponent) {
  float x = 0;
  float y = 0;
  if (dna != null) {
    x = cos(dna[0]*2*PI)*dna[1];
    y = sin(dna[0]*2*PI)*dna[1];
  }

  float ang = 0;
  float dist_ = -1;
  while (dist(0, 0, x+cos(ang*2*PI)*dist_, y+sin(ang*2*PI)*dist_) >= 1.0 || dist_ < 0) {
    dist_ = pow(random(0, amount), exponent);
    ang = random(0, 1);
  }
  float new_x = x+cos(ang*2*PI)*dist_;
  float new_y = y+sin(ang*2*PI)*dist_;
  float new_ang = atan3(new_y, new_x)/(2*PI);
  float new_dist = dist(0, 0, new_x, new_y);
  float[] result = {new_ang, new_dist};
  return result;
}

float[] dnaAverage(float[] dna1, float[] dna2) {
  float x1 = cos(dna1[0]*2*PI)*dna1[1];
  float y1 = sin(dna1[0]*2*PI)*dna1[1];
  float x2 = cos(dna2[0]*2*PI)*dna2[1];
  float y2 = sin(dna2[0]*2*PI)*dna2[1];
  float new_x = (x1+x2)/2;
  float new_y = (y1+y2)/2;
  float new_ang = atan3(new_y, new_x)/(2*PI);
  float new_dist = dist(0, 0, new_x, new_y);
  float[] result = {new_ang, new_dist};
  return result;
}

float dnaDistance(float[] dna1, float[] dna2) {
  float x1 = cos(dna1[0]*2*PI)*dna1[1];
  float y1 = sin(dna1[0]*2*PI)*dna1[1];
  float x2 = cos(dna2[0]*2*PI)*dna2[1];
  float y2 = sin(dna2[0]*2*PI)*dna2[1];
  return dist(x1,y1,x2,y2);
}


void doPhysics() {
  for (int p = 0; p < playback_speed; p++) {
    ticks++;
  }
  for (int i = 0; i < players.size(); i++) {
    players.get(i).doActions(keyHandler);
  }
  for (int i = 0; i < players.size(); i++) {
    players.get(i).doPhysics(map);
  }
  for (int i = 0; i < guts.size(); i++) {
    guts.get(i).doPhysics();
  }
  if (!followSpecimen) {
    getClosestAI();
  }
}
public double sigmoid(double input) {
  return 1.0/(1.0+Math.pow(2.71828182846, -input));
}
void getClosestAI() {
  closest_AI = getClosestAIto(camera, players.get(0), 3.0*T);
}
Player getClosestAIto(float[] loc, Player ignore, float MAX_DIST) {
  Player cAI = null;
  float record = 9999999;
  for (int i = 0; i < players.size(); i++) {
    Player p = players.get(i);
    if (getSpeciesType(p.species) <= 0 || p == ignore) {
      continue;
    }
    float dist_ = d_loop(players.get(i).coor, loc, false);
    if (dist_ < record) {
      record = dist_;
      cAI = players.get(i);
    }
  }
  if (record >= MAX_DIST) {
    cAI = null;
  }
  return cAI;
}

float d(float[] c1, float[] c2) {
  return dist(c1[0], c1[1], c2[0], c2[1]);
}
float d_loop(float[] c1, float[] c2, boolean include_3rd) {
  float dx = unloop(c1[0]-c2[0]);
  float dy = unloop(c1[1]-c2[1]);
  float dz = 0;
  if (include_3rd) {
    dz = c1[2]-c2[2];
  }
  return dist(dx, dy, dz, 0, 0, 0);
}
float unloop(float val) {
  while (val <= -SIZE*T/2) {
    val += SIZE*T;
  }
  while (val > SIZE*T/2) {
    val -= SIZE*T;
  }
  return val;
}
float unloop_arr(float val) {
  while (val < 0) {
    val += SIZE*T;
  }
  while (val >= SIZE*T) {
    val -= SIZE*T;
  }
  return val;
}

int unloop_int(int val, int N) {
  while (val < 0) {
    val += N;
  }
  while (val >= N) {
    val -= N;
  }
  return val;
}
float unloop_angle(float val) {
  while (val <= -PI) {
    val += PI*2;
  }
  while (val > PI) {
    val -= PI*2;
  }
  return val;
}
float unloop_two(float val, float target) {
  while (val-target <= -SIZE*T/2) {
    val += SIZE*T;
  }
  while (val-target > SIZE*T/2) {
    val -= SIZE*T;
  }
  return val;
}

boolean mapVisible(float[] coor) {
  float dist_ = d_loop(camera, coor, true);
  if (dist_ < 3*T) {
    return true;
  }
  int x = (int)min(max(coor[0]/T, 0), SIZE-EPS);
  int y = (int)min(max(coor[1]/T, 0), SIZE-EPS);
  return map.visible[x][y][0];
}

void drawVisuals() {
  clearUI();
  g.beginDraw();
  g.sphereDetail(8);
  g.noStroke();
  g.lights();
  g.background(darken(SKY_COLOR, pow(daylight(), 0.5)));
  g.pushMatrix();
  g.translate(g.width/2, g.height/2, 0);
  for (int i = 0; i < players.size(); i++) {
    if ((!followSpecimen && players.get(i).species == -1) ||
      (followSpecimen && players.get(i) == closest_AI)) {
      players.get(i).snapCamera(!followSpecimen);
    }
  }
  g.directionalLight(26, 51, 63, -0.934, -0.37, 0);
  g.directionalLight(26, 51, 63, 1.2, 0.55, 0);
  g.directionalLight(50, 50, 50, 0, 0, -1);
  map.drawMap();
  for (int i = 0; i < players.size(); i++) {
    players.get(i).drawPlayer();
  }
  for (int i = 0; i < guts.size(); i++) {
    guts.get(i).drawGut();
  }
  g.popMatrix();
  g.endDraw();
}

void clearUI() {
  ui.beginDraw();
  ui.background(0);
  ui.endDraw();
}

color darken(color c, float perc) {
  float newR = red(c)*perc;
  float newG = green(c)*perc;
  float newB = blue(c)*perc;
  return color(newR, newG, newB);
}
color lighten(color c, float perc) {
  float newR = 255-(255-red(c))*perc;
  float newG = 255-(255-green(c))*perc;
  float newB = 255-(255-blue(c))*perc;
  return color(newR, newG, newB);
}
float[] deepCopy(float[] input) {
  float[] result = new float[input.length];
  for (int i = 0; i < input.length; i++) {
    result[i] = input[i];
  }
  return result;
}
double[] deepCopy(double[] input) {
  double[] result = new double[input.length];
  for (int i = 0; i < input.length; i++) {
    result[i] = input[i];
  }
  return result;
}

void deepCopy(float[] input, float[] output) {
  for (int i = 0; i < input.length; i++) {
    output[i] = input[i];
  }
}

float[][] deepCopy(float[][] input) {
  float[][] result = new float[input.length][input[0].length];
  for (int i = 0; i < input.length; i++) {
    for (int j = 0; j < input[i].length; j++) {
      result[i][j] = input[i][j];
    }
  }
  return result;
}

float ticksToDays(float age) {
  return age/TICKS_PER_DAY;
}
void keyPressed() {
  keyHandler.handle(keyCode, true);
}
void keyReleased() {
  keyHandler.handle(keyCode, false);
}
void mousePressed() {
  sfx[5].play();
  /*g.pushMatrix();
   g.translate(width/2,height/2,0);
   for(int i = 0; i < players.size(); i++){
   players.get(i).snapCamera();
   }
   g.popMatrix();*/
}
void doMouse() {
  if (TRAP_MOUSE) {
    if (frames >= 2) {
      camera[3] += (mouseX-CENTER_X)*0.005;
      camera[4] += (mouseY-g.height/2)*0.005;
    }
    r.warpPointer(g.width/2, g.height/2);
  }
}
color color_lerp(color a, color b, float x) {
  float newR = red(a)+(red(b)-red(a))*x;
  float newG = green(a)+(green(b)-green(a))*x;
  float newB = blue(a)+(blue(b)-blue(a))*x;
  return color(newR, newG, newB);
}
int getSpeciesType(int n) {
  int[] IS_PLANT = {0, 1, 2};
  if (n == -1 || n == -2) {
    return -1;
  }
  return IS_PLANT[n];
}
void mouseWheel(MouseEvent event) {
  float e = event.getCount();
  if (e < -0.5) {
    DISTANCE_FROM_PLAYER /= 1.1;
  } else if (e >= 0.5) {
    DISTANCE_FROM_PLAYER *= 1.1;
  }
}
