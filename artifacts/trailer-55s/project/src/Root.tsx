import React from 'react';
import {Composition} from 'remotion';
import {Film} from './Film';
import {TOTAL} from './timeline';

export const Root:React.FC=()=> <>
 <Composition id="Landscape" component={Film} durationInFrames={TOTAL} width={1920} height={1080} fps={30} defaultProps={{bgm:true,sound:true}}/>
 <Composition id="Vertical" component={Film} durationInFrames={TOTAL} width={1080} height={1920} fps={30} defaultProps={{bgm:true,sound:true}}/>
</>;
