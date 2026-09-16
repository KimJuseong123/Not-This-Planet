// Animate the ImageGen master as a periodic 2D motion concept, not a fluid simulation.
// Usage: node make-loop.cjs <output-directory> [absolute-sharp-module-path]
const fs = require('node:fs');
const path = require('node:path');
const sharp = require(process.argv[3] || 'sharp');
const out = process.argv[2];
const W=960, H=400, N=120, DELAY=50, TAU=Math.PI*2;
const smooth=(a,b,v)=>{const t=Math.max(0,Math.min(1,(v-a)/(b-a)));return t*t*(3-2*t)};
(async()=>{
 const {data:src}=await sharp(path.join(out,'magnetic-storm-front.png')).resize(W,H,{fit:'fill'}).removeAlpha().raw().toBuffer({resolveWithObject:true});
 const mask=new Float32Array(W*H), cyan=new Float32Array(W*H);
 for(let y=0;y<H;y++)for(let x=0;x<W;x++){
  const p=y*W+x,i=p*3, r=src[i],g=src[i+1],b=src[i+2];
  mask[p]=smooth(35,100,Math.max(r,g))*smooth(.14,.28,y/H)*(1-smooth(.677,.714,y/H))*smooth(0,.045,x/W)*(1-smooth(.955,1,x/W));
  cyan[p]=smooth(5,60,g-r)*smooth(95,175,g)*mask[p];
 }
 const maskBytes=Buffer.from(Uint8Array.from(mask, v=>Math.round(v*255)));
 const blurred=await sharp(maskBytes,{raw:{width:W,height:H,channels:1}}).blur(12).raw().toBuffer();
 const maskChannels=blurred.length/(W*H);
 for(let p=0;p<mask.length;p++) mask[p]=blurred[p*maskChannels]/255;
 function frame(t){
  t=((t%TAU)+TAU)%TAU;
  const dst=Buffer.alloc(W*H*3);
  for(let y=0;y<H;y++){
   const v=y/H, sy1=Math.sin(v*22-t),sy2=Math.cos(v*34+2*t);
   for(let x=0;x<W;x++){
    const p=y*W+x,i=p*3,u=x/W,m=mask[p];
    const dx=m*(8*sy1+4*Math.sin(u*21+v*13-2*t));
    const dy=m*(5*Math.sin(u*23-t)+2.6*sy2);
    const xx=Math.max(0,Math.min(W-1.001,x+dx)),yy=Math.max(0,Math.min(H-1.001,y+dy));
    const ix=Math.floor(xx),iy=Math.floor(yy),fx=xx-ix,fy=yy-iy;
    const a=(iy*W+ix)*3,b=a+3,c=a+W*3,d=c+3;
    const shimmer=1 + cyan[p]*(.18*Math.sin(u*22+v*9-2*t)+.075*Math.sin(u*45+3*t))
                   + m*.023*Math.sin(u*18+v*20-t);
    for(let ch=0;ch<3;ch++){
     const val=(src[a+ch]*(1-fx)*(1-fy)+src[b+ch]*fx*(1-fy)+src[c+ch]*(1-fx)*fy+src[d+ch]*fx*fy)*shimmer;
     dst[i+ch]=Math.max(0,Math.min(255,Math.round(val)));
    }
   }
  }
  // Small energy motes move on closed flux paths, returning smoothly after 6 seconds.
  for(let j=0;j<28;j++){
   const group=j%5, phase=t*(j%3===0?2:1)+j*2.399963229728653;
   const cx=W*(.15+group*.175), cy=H*(.50+.025*Math.sin(j));
   const rx=W*(.042+.012*(j%3)),ry=H*(.065+.012*(j%4));
   const px=cx+rx*Math.cos(phase),py=cy+ry*Math.sin(phase)+H*.018*Math.sin(2*phase);
   const strength=.24+.40*(.5+.5*Math.sin(phase+j));
   for(let oy=-4;oy<=4;oy++)for(let ox=-4;ox<=4;ox++){
    const xx=Math.round(px)+ox,yy=Math.round(py)+oy;
    if(xx<0||xx>=W||yy<0||yy>=H)continue;
    const power=Math.exp(-(ox*ox+oy*oy)/3)*strength;
    const i=(yy*W+xx)*3;
    dst[i]=Math.min(255,dst[i]+32*power);
    dst[i+1]=Math.min(255,dst[i+1]+130*power);
    dst[i+2]=Math.min(255,dst[i+2]+155*power);
   }
  }
  return dst;
 }
 const first=frame(0), endpoint=frame(TAU);
 let endpointMax=0;
 for(let i=0;i<first.length;i++) endpointMax=Math.max(endpointMax,Math.abs(first[i]-endpoint[i]));
 if(endpointMax!==0)throw Error('Period endpoint mismatch');
 const strip=Buffer.alloc(W*H*3*N),deltas=[];
 let previous=null,last=null;
 for(let k=0;k<N;k++){
  const f=frame(TAU*k/N);
  f.copy(strip,k*f.length);
  if(previous){let d=0;for(let i=0;i<f.length;i++)d+=Math.abs(f[i]-previous[i]);deltas.push(d/f.length);}
  previous=f;last=f;
  if(k%30===0)console.log('Rendered '+k+'/'+N);
  if([0,30,60,90,119].includes(k))await sharp(f,{raw:{width:W,height:H,channels:3}}).png().toFile(path.join(out,'_qa-frame-'+k+'.png'));
 }
 let seam=0;for(let i=0;i<first.length;i++)seam+=Math.abs(last[i]-first[i]);seam/=first.length;
 const file=path.join(out,'magnetic-storm-loop.gif');
 await sharp(strip,{raw:{width:W,height:H*N,channels:3,pageHeight:H},limitInputPixels:false})
  .gif({loop:0,delay:Array(N).fill(DELAY),colours:256,effort:4,dither:.3,interFrameMaxError:0,interPaletteMaxError:0})
  .toFile(file);
 const meta=await sharp(file,{animated:true}).metadata();
 const avg=deltas.reduce((a,b)=>a+b,0)/deltas.length;
 const report={width:meta.width,height:meta.pageHeight,frames:meta.pages,loop:meta.loop,
 durationMs:meta.delay.reduce((a,b)=>a+b,0),frameDelayMs:DELAY,endpointMaxChannelError:endpointMax,
 seamMeanAbsoluteError:seam,averageAdjacentMeanAbsoluteError:avg,seamToAverageRatio:seam/avg,
 sizeBytes:fs.statSync(file).size,method:'Periodic image-space billow deformation, travelling cyan emission and closed-path motes. Fixed camera. Not a 3D fluid simulation.'};
 if(meta.pages!==N||meta.loop!==0||report.durationMs!==6000||seam/avg>1.5)throw Error(JSON.stringify(report));
 fs.writeFileSync(path.join(out,'loop-validation.json'),JSON.stringify(report,null,2));
 console.log(JSON.stringify(report,null,2));
})().catch(e=>{console.error(e);process.exit(1)});
