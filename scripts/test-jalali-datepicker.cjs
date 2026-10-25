// Pure JS regression checks for the shared Persian calendar in HRPortal.
// Run with: node scripts/test-jalali-datepicker.cjs
const fs=require('node:fs');
const vm=require('node:vm');
const assert=require('node:assert/strict');
const source=fs.readFileSync('src/HRPortal/wwwroot/js/jalali-datepicker.js','utf8');
const context={
  Date,Intl,Event:function(){},
  window:{},
  document:{addEventListener(){}}
};
vm.runInNewContext(source,context,{filename:'jalali-datepicker.js'});
const {g2j,j2g,parse,dim}=context.window.HRPortalJalali;
const cases=[
  {j:[1405,1,1],g:'2026-03-21'},
  {j:[1405,6,21],g:'2026-09-12'},
  {j:[1405,6,25],g:'2026-09-16'},
  {j:[1405,12,29],g:'2027-03-20'},
  {j:[1403,12,30],g:'2025-03-20'}
];
for(const {j,g} of cases){
 const d=j2g(...j);
 assert.equal(d.toISOString().slice(0,10),g);
 const back=g2j(d);
 assert.equal(back.y,j[0]);assert.equal(back.m,j[1]);assert.equal(back.day,j[2]);
}
assert.equal(dim(1405,6),31);
assert.equal(dim(1405,12),29);
assert.equal(dim(1403,12),30);
assert.equal(parse('۱۴۰۵/۰۶/۲۱').day,21);
assert.equal(parse('١٤٠٥/٠٦/٢٥').day,25);
assert.equal(parse('1404/12/30'),null);
assert.equal(parse('1405/13/01'),null);
assert.equal(parse('anything'),null);
console.log('Persian calendar date conversion tests passed.');
