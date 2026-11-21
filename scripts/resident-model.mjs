// 居民底模 · 第 1 步：基模（只有身体，无衣服无帽子）
// 体素：整数格立方体，无倒角、无斜面。一格 = 1/32，身高 32 格 = 1.00。
// 坐标：x 左右，y 上下（0 = 脚底），z 前后（+z 正面）。区间半开 [a,b)。

export const UNIT = 1 / 32;

export const PALETTE = {
  skin: '#EFC49C',
  hair: '#4A2E20',
  eye:  '#241B16'
};

const R = (x0,x1,y0,y1,z0,z1,slot) => ({x0,x1,y0,y1,z0,z1,slot});

export const PART_ORDER = ['head','torso','armL','armR','legL','legR'];

export const PARTS = {
  // 头 16 x 11 x 16，Y 21-32
  head: {
    label: '头部',
    boxes: [
      R(-7,7, 21,32, -7,7, 'skin'),
      R(-8,8, 28,32, -8,8, 'hair'),
      R(-8,8, 21,28, -8,-4, 'hair'),
      R(-8,-5, 23,28, -5,6, 'hair'),
      R( 5, 8, 23,28, -5,6, 'hair'),
      R(-8,8, 28,29, 4,8, 'hair'),
      R(-4,-2, 25,28, 7,8, 'eye'),
      R( 2, 4, 25,28, 7,8, 'eye')
    ]
  },
  // 躯干：肩 21 往下延伸，包过胯部到 8（腿顶）
  torso: {
    label: '躯干',
    boxes: [ R(-6,6, 8,21, -4,4, 'skin') ]
  },
  // 左臂：长度保持 14 格（Y 6-20），只把位置外移 1 格做间隙
  armL: {
    label: '左臂',
    boxes: [ R(-10,-7, 7,21, -3,2, 'skin') ]
  },
  // 右臂
  armR: {
    label: '右臂',
    boxes: [ R( 7, 10, 7,21, -3,2, 'skin') ]
  },
  // 左腿：8 格高，Y 0-8
  legL: {
    label: '左腿',
    boxes: [ R(-6,-1, 0,8, -3,3, 'skin') ]
  },
  // 右腿：8 格高
  legR: {
    label: '右腿',
    boxes: [ R( 1, 6, 0,8, -3,3, 'skin') ]
  }
};