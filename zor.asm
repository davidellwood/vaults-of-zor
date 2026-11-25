; ===========================================================================
;  VAULTS OF ZOR  -  a maze adventure for the TRS-80 Model I (16K+, Level II)
;  Z80 machine code. Assemble with zmac:   zmac zor.asm
;  Run in trs80gp:                         trs80gp -m1 zout/zor.cmd
;
;  Screen (64 x 16 characters) is split into three windows:
;
;    col 0-31            col 32   col 34-63
;   +-------------------+  |  +------------------------+
;   |                   |  |  | MAP (15x7 cells)       |  rows 0-6
;   |  3D VIEW          |  |  |-- status line ---------|  row 7
;   |  32x16 chars      |  |  | TEXT OUTPUT (7 lines)  |  rows 8-14
;   |  (64x48 pixels)   |  |  | > COMMAND INPUT        |  row 15
;   +-------------------+  |  +------------------------+
;
;  The 3D view is assembled from pre-rasterised wall segments (see gen.py)
;  into an off-screen buffer, sprites are masked on top, then the buffer is
;  copied to video RAM in one go so the view never flickers.
; ===========================================================================

VIDEO	equ	3C00h		; Model I video RAM
KBD	equ	002Bh		; ROM: scan keyboard, A = key or 0

TXTCOL	equ	34		; text window
TXTW	equ	30
TXTTOP	equ	8
TXTBOT	equ	14
MAPCOL	equ	34		; map window
STROW	equ	7		; status line

; maze cell byte: bit 7 = seen, bit 6 = wall (terrain), bit 5 = blocked
; (wall or a solid object), bits 0-4 = look: what the view and map show there.
; The looks are the engine's pictures; objects defined in the pack pick one.
C_FLOOR	equ	0		; nothing (bare floor, or an invisible object)
C_WALL	equ	1		; drawn as a wall face
C_DOOR	equ	2		; drawn as a door face
C_LADDER equ	3		; 3-19: sprites
C_SECRET equ	15		; (unused look)
MAXINST	equ	64		; objects per level
MAXTYPES equ	128		; object types per pack

FRAMEDLY equ	60		; animation pause: 16 keyboard polls x this loop (~17 ms)

; TRSDOS 2.3 (Model I) entry points
@EXIT	equ	402Dh		; return to TRSDOS READY
@FSPEC	equ	441Ch		; HL = filespec text, DE = FCB
@OPEN	equ	4424h		; DE = FCB, HL = 256-byte buffer, B = LRL
@CLOSE	equ	4428h		; DE = FCB
@READ	equ	4436h		; DE = FCB, next record into buffer
HIGH$	equ	4049h		; top of free memory
CMDBUF	equ	43h		; high byte of the DOS command line buffer (4318h)

; level pack header offsets, format version 5 (pointers are offsets from the
; pack start, 0 = none). See DESIGN-OBJECTS.md.
PK_LEN	equ	8		; dw total length
PK_NLEV	equ	10		; db number of levels
PK_HP	equ	11		; db start hp, max hp, number of types,
				;    status counter (255 = none), 2-char status label, spare
PK_TITLE equ	18		; dw -> 1024-byte title screen
PK_VICT	equ	20		; dw -> victory text
PK_VOCAB equ	22		; dw -> vocabulary
PK_TYPES equ	24		; dw -> object types, 6 bytes each
PK_RULES equ	26		; dw -> pack rules (object type behaviours)
PK_TEXTS equ	28		; dw -> pack texts
PK_LEVS	equ	30		; dw x n -> level records
; level record: db x,y,dir,spare ; dw name ; dw intro ; dw instances ;
;               dw rules ; dw texts ; 96 bytes terrain (1 bit per cell)

	org	5200h

entry:	ld	sp,STACK
	ld	(tailp),hl
	xor	a
	ld	(errcode),a
	ld	(noshow),a
	ld	(kqn),a
	ld	(keepview),a	; TRSDOS passes the command line tail in HL
	ld	hl,0ACE1h
	ld	(seed),hl
	call	findpack

; ---------------------------------------------------------------- title
gotitle:
	ld	sp,STACK
	ld	a,PK_TITLE
	call	packw
	ld	de,VIDEO
	ld	bc,1024
	ldir
tt1:	call	waitkey
	cp	01h		; BREAK: back to TRSDOS
	jp	z,exitdos
	cp	0Dh
	jr	nz,tt1

	jp	newgame

mainlp:	ld	a,(keepview)	; a speech bubble stays up until the next action
	or	a
	call	z,render
	xor	a
	ld	(keepview),a
	call	kpoll
	call	drawmap
	call	status
	call	kpoll
	call	command
	jr	mainlp

; ===========================================================================
;  Keyboard and random numbers
; ===========================================================================
inkey:	push	bc
	push	de
	push	hl
	call	KBD
	pop	hl
	pop	de
	pop	bc
	ret

waitkey:			; next key: from the type-ahead queue, else the keyboard
	ld	a,(kqn)
	or	a
	jr	z,wk_1
	push	bc
	push	de
	ld	a,(KQ)
	push	af
	ld	hl,KQ+1
	ld	de,KQ
	ld	bc,15
	ldir
	ld	hl,kqn
	dec	(hl)
	pop	af
	pop	de
	pop	bc
	ret
wk_1:	call	rnd		; wait for a key, stirring the RNG meanwhile
	call	inkey
	or	a
	jr	z,wk_1
	ret

waitenter:
	call	waitkey
	cp	0Dh
	jr	nz,waitenter
	ret

rnd:				; 16-bit xorshift, result in HL (and seed)
	ld	hl,(seed)
	ld	a,h
	rra
	ld	a,l
	rra
	xor	h
	ld	h,a
	ld	a,l
	rra
	ld	a,h
	rra
	xor	l
	ld	l,a
	xor	h
	ld	h,a
	ld	(seed),hl
	ret

; ===========================================================================
;  Level packs
; ===========================================================================
; Pick the level pack: a file named on the command line ("ZOR MAZES/LEV"),
; else the pack built into this /CMD file, else ZOR/LEV from disk.
findpack:
	ld	hl,BUILTIN	; the disk buffer lives just past the built-in pack
	ld	de,(BUILTIN+PK_LEN)
	add	hl,de
	inc	h		; round up to a page boundary
	ld	l,0
	ld	(packbuf),hl
	ld	hl,(tailp)
	ld	a,h
	cp	CMDBUF
	jr	nz,fp_1
	ld	a,(hl)
	cp	'A'
	jr	c,fp_1
	cp	'Z'+1
	jr	c,loadpack
fp_1:	ld	a,(BUILTIN+PK_NLEV)
	or	a
	jr	z,fp_2
	ld	hl,BUILTIN
	ld	(packp),hl
	ret
fp_2:	ld	hl,DEFNAME
				; fall into loadpack
loadpack:			; HL = filespec. Loads the pack into packbuf.
	ld	de,FCB
	call	@FSPEC
	ld	a,13h		; "illegal file name"
	jp	nz,diskerr
	ld	hl,SECBUF
	ld	de,FCB
	ld	b,0		; 256-byte records
	call	@OPEN
	jr	z,lp_ok
	cp	2Ah		; LRL mismatch is only a warning
	jp	nz,diskerr
lp_ok:	ld	hl,(packbuf)
	ld	(dest),hl
lp_rd:	ld	de,FCB
	call	@READ
	jr	nz,lp_eof
	ld	hl,(dest)	; room for another sector?
	ld	de,256
	add	hl,de
	ex	de,hl
	call	memtop
	or	a
	sbc	hl,de
	jr	c,lp_big
	ld	hl,SECBUF
	ld	de,(dest)
	ld	bc,256
	ldir
	ld	(dest),de
	jr	lp_rd
lp_eof:	ld	de,FCB
	call	@CLOSE
	ld	hl,(packbuf)	; check signature and length
	ld	de,PKSIG
	ld	b,8
lp_sig:	ld	a,(de)
	cp	(hl)
	jr	nz,lp_bad
	inc	hl
	inc	de
	djnz	lp_sig
	ld	hl,(packbuf)
	ld	(packp),hl
	ld	de,(dest)	; loaded bytes must cover the stated length
	ex	de,hl
	or	a
	sbc	hl,de		; HL = bytes loaded
	ex	de,hl
	ld	hl,(packbuf)
	ld	bc,PK_LEN
	add	hl,bc
	ld	a,(hl)
	inc	hl
	ld	h,(hl)
	ld	l,a		; HL = stated length
	ex	de,hl
	or	a
	sbc	hl,de
	jr	c,lp_bad
	ld	hl,(packp)
	ld	de,PK_NLEV
	add	hl,de
	ld	a,(hl)
	or	a
	jr	z,lp_bad
	ret
lp_big:	ld	hl,M_BIG
	jr	fatal
lp_bad:	ld	hl,M_BADPK
	jr	fatal

memtop:				; HL = highest usable address
	ld	hl,(HIGH$)
	ld	a,h
	cp	80h		; implausible value: assume 48K
	ret	nc
	ld	hl,0FF00h
	ret

diskerr:			; A = TRSDOS error code
	ld	(errcode),a
	ld	hl,M_DISK
	cp	18h		; "file not in directory"
	jr	nz,fatal
	xor	a
	ld	(errcode),a
	ld	hl,M_NOTFND
fatal:				; show message HL on a clear screen, then exit
	push	hl
	call	cls
	ld	a,1
	ld	(omode),a
	ld	hl,VIDEO+64*6+4
	ld	(vptr),hl
	pop	hl
	call	msgc
	ld	a,(errcode)
	or	a
	jr	z,fa_1
	ld	hl,M_ERRNO
	call	msgc
	ld	a,(errcode)
	call	putdeca
fa_1:	ld	hl,VIDEO+64*8+4
	ld	(vptr),hl
	ld	hl,M_ANYKEY
	call	msgc
	call	waitkey
exitdos:
	call	cls
	jp	@EXIT

packw:				; HL = pack address of the word at header offset A
	ld	hl,(packp)
	push	hl
	call	addhla
	ld	e,(hl)
	inc	hl
	ld	d,(hl)
	pop	hl
	add	hl,de
	ret


ll_ptr:				; DE = pack address of the word at HL (0 stays 0); HL += 2
	ld	e,(hl)
	inc	hl
	ld	d,(hl)
	inc	hl
	ld	a,d
	or	e
	ret	z
	push	hl
	ld	hl,(packp)
	add	hl,de
	ex	de,hl
	pop	hl
	ret

packoff:			; HL = pack start + HL
	ld	de,(packp)
	add	hl,de
	ret

; ===========================================================================
;  Screen helpers
; ===========================================================================
cls:	ld	hl,VIDEO
	ld	(hl),20h
	ld	de,VIDEO+1
	ld	bc,1023
	ldir
	ret

frame:	ld	hl,VIDEO+32	; vertical divider
	ld	de,64
	ld	b,16
fr_1:	ld	(hl),0AAh
	add	hl,de
	djnz	fr_1
	ret

; ---- text window output -------------------------------------------------
; omode = 0: characters go to the scrolling text window
; omode = 1: characters go to (vptr) - used for the status line
txaddr:	ld	a,(trow)	; HL = video address of text cursor
	ld	l,a
	ld	h,0
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	ld	de,VIDEO+TXTCOL
	add	hl,de
	ld	a,(tcol)
	ld	e,a
	ld	d,0
	add	hl,de
	ret

newline:
	push	af
	push	bc
	push	de
	push	hl
	xor	a
	ld	(tcol),a
	ld	a,(trow)
	cp	TXTBOT
	jr	nc,nl_scr
	inc	a
	ld	(trow),a
	jr	nl_clr
nl_scr:	ld	hl,VIDEO+(TXTTOP+1)*64+TXTCOL
	ld	de,VIDEO+TXTTOP*64+TXTCOL
	ld	a,TXTBOT-TXTTOP
nl_s1:	push	af
	push	hl
	push	de
	ld	bc,TXTW
	ldir
	pop	de
	pop	hl
	ld	bc,64
	add	hl,bc
	ex	de,hl
	add	hl,bc
	ex	de,hl
	pop	af
	dec	a
	jr	nz,nl_s1
nl_clr:	call	txaddr
	ld	b,TXTW
nl_c1:	ld	(hl),20h
	inc	hl
	djnz	nl_c1
	pop	hl
	pop	de
	pop	bc
	pop	af
	ret

putc:	push	af		; print character A
	ld	a,(omode)
	or	a
	jr	nz,putv
	pop	af
	push	hl
	push	de
	push	af
	ld	a,(tcol)
	cp	TXTW
	call	nc,newline
	call	txaddr
	pop	af
	ld	(hl),a
	ld	hl,tcol
	inc	(hl)
	pop	de
	pop	hl
	ret
putv:	pop	af
	push	hl
	ld	hl,(vptr)
	ld	(hl),a
	inc	hl
	ld	(vptr),hl
	pop	hl
	ret

msg:	call	newline		; print string HL on a new line
msgc:	ld	a,(hl)		; continue printing string HL (0 or 2 = end, 13 or 1 = newline)
	inc	hl
	or	a
	ret	z
	cp	2		; end of one part of an object's text
	ret	z
	cp	0Dh
	jr	z,mc_nl
	cp	1
	jr	z,mc_nl
	call	putc
	jr	msgc
mc_nl:	call	newline
	jr	msgc

putdec:				; print HL in decimal, no leading zeros
	push	af
	push	bc
	push	de
	push	hl
	ld	e,0
	ld	bc,-10000
	call	pd_1
	ld	bc,-1000
	call	pd_1
	ld	bc,-100
	call	pd_1
	ld	bc,-10
	call	pd_1
	ld	a,l
	add	a,'0'
	call	putc
	pop	hl
	pop	de
	pop	bc
	pop	af
	ret
pd_1:	ld	a,'0'-1
pd_2:	inc	a
	add	hl,bc
	jr	c,pd_2
	sbc	hl,bc
	cp	'0'
	jr	nz,pd_3
	inc	e
	dec	e
	ret	z
pd_3:	ld	e,1
	jp	putc

putdeca:			; print A in decimal
	push	hl
	ld	l,a
	ld	h,0
	call	putdec
	pop	hl
	ret

; ===========================================================================
;  Maze helpers
; ===========================================================================
caddr:				; E = x, D = y -> HL = cell address, or 0 if outside
	ld	a,e
	cp	32
	jr	nc,ca_0
	ld	a,d
	cp	24
	jr	nc,ca_0
	ld	l,d
	ld	h,0
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	ld	a,e
	add	a,l
	ld	l,a
	ld	de,MAZE
	add	hl,de
	ret
ca_0:	ld	hl,0
	ret

herecode:			; A = code of player's cell, HL = its address
	ld	a,(px)
	ld	e,a
	ld	a,(py)
	ld	d,a
	call	caddr
	ld	a,(hl)
	and	1Fh
	ret

getvec:				; A = direction -> B = dx, C = dy
	and	3
	ld	hl,DXT
	call	addhla
	ld	b,(hl)
	inc	hl
	inc	hl
	inc	hl
	inc	hl
	ld	c,(hl)
	ret

addhla:	add	a,l		; HL += A
	ld	l,a
	adc	a,h
	sub	l
	ld	h,a
	ret

ptrtab:				; HL = word at table HL + 2*A
	add	a,a
	call	addhla
	ld	a,(hl)
	inc	hl
	ld	h,(hl)
	ld	l,a
	ret

mul3:	ld	d,a
	add	a,a
	add	a,d
	ret

; view cell table VA: 5 depths x (left, centre, right) addresses
cellv:				; A = view index -> A = code (wall if outside), HL = addr
	ld	hl,VA
	call	ptrtab
	ld	a,h
	or	l
	ld	a,C_WALL
	ret	z
	ld	a,(hl)
	and	1Fh
	ret

markv:				; mark view cell A as seen
	call	cellv
	ld	a,h
	or	l
	ret	z
	set	7,(hl)
	ret

solid:				; Z set if code A looks solid (wall, door, secret wall)
	cp	C_WALL
	ret	z
	cp	C_DOOR
	ret	z
	cp	C_SECRET
	ret

; ===========================================================================
;  3D view renderer
; ===========================================================================
render:				; draw the view from (px,py) facing (dir)
	xor	a
renderp:			; A = phase 0-3: view A/4 of a step further forward
	ld	l,a		; select that phase's segment and sprite tables
	add	a,a
	add	a,a
	add	a,l
	add	a,a		; A * 10
	ld	hl,PHASETAB
	call	addhla
	ld	de,gSEGL
	ld	bc,10
	ldir
	ld	a,(dir)
	call	getvec
	ld	a,b
	ld	(fx),a
	ld	a,c
	ld	(fy),a
	ld	a,(dir)
	add	a,3
	call	getvec
	ld	a,b
	ld	(lfx),a
	ld	a,c
	ld	(lfy),a
; build the table of visible cell addresses
	ld	a,(px)
	ld	(cx),a
	ld	a,(py)
	ld	(cy),a
	ld	ix,VA
	ld	b,5
rv_1:	push	bc
	ld	a,(lfx)		; left
	ld	b,a
	ld	a,(cx)
	add	a,b
	ld	e,a
	ld	a,(lfy)
	ld	b,a
	ld	a,(cy)
	add	a,b
	ld	d,a
	call	caddr
	ld	(ix+0),l
	ld	(ix+1),h
	ld	a,(cx)		; centre
	ld	e,a
	ld	a,(cy)
	ld	d,a
	call	caddr
	ld	(ix+2),l
	ld	(ix+3),h
	ld	a,(lfx)		; right
	ld	b,a
	ld	a,(cx)
	sub	b
	ld	e,a
	ld	a,(lfy)
	ld	b,a
	ld	a,(cy)
	sub	b
	ld	d,a
	call	caddr
	ld	(ix+4),l
	ld	(ix+5),h
	ld	a,(fx)		; step forward
	ld	b,a
	ld	a,(cx)
	add	a,b
	ld	(cx),a
	ld	a,(fy)
	ld	b,a
	ld	a,(cy)
	add	a,b
	ld	(cy),a
	ld	de,6
	add	ix,de
	pop	bc
	djnz	rv_1
	call	kpoll
; find the first solid cell straight ahead (1..4, 5 = none)
	ld	b,1
rf_1:	ld	a,b
	call	mul3
	inc	a
	call	cellv
	call	solid
	jr	z,rf_2
	inc	b
	ld	a,b
	cp	5
	jr	c,rf_1
rf_2:	ld	a,b
	ld	(front),a
	cp	5
	jr	c,rf_3
	ld	a,4
rf_3:	ld	(kmax),a
; mark centre cells 0..kmax as seen
	ld	b,0
rm_1:	ld	a,b
	call	mul3
	inc	a
	call	markv
	inc	b
	ld	a,(kmax)
	cp	b
	jr	nc,rm_1
; side segments for depth 0 .. kmax-1
	ld	b,0
rs_1:	ld	a,(kmax)
	cp	b
	jr	z,rs_done
	ld	a,b
	call	mul3
	ld	(segi),a
	push	bc
	ld	c,0		; left
	call	sidestate
	ld	hl,segi
	add	a,(hl)
	ld	hl,(gSEGL)
	call	ptrtab
	call	blit
	pop	bc
	push	bc
	ld	c,2		; right
	call	sidestate
	ld	hl,segi
	add	a,(hl)
	ld	hl,(gSEGR)
	call	ptrtab
	call	blit
	pop	bc
	inc	b
	jr	rs_1
rs_done:
	call	kpoll
; front face (or blank centre if nothing within range)
	ld	a,(front)
	cp	5
	jr	c,rr_f
	ld	hl,(gF5)
	call	blit
	jr	rr_spr
rr_f:	ld	b,a
	call	mul3
	inc	a
	call	cellv
	ld	c,0
	cp	C_DOOR
	jr	nz,rr_f1
	inc	c
rr_f1:	ld	a,b
	dec	a
	add	a,a
	add	a,c
	ld	hl,(gSEGF)
	call	ptrtab
	call	blit
; nearest object straight ahead, depth 1 .. min(front-1, 3)
rr_spr:	ld	b,1
rr_s1:	ld	a,(front)
	cp	b
	jr	z,rr_copy
	jr	c,rr_copy
	ld	a,b
	cp	4
	jr	nc,rr_copy
	call	mul3
	inc	a
	call	cellv
	or	a
	jr	nz,rr_s2
	inc	b		; nothing to see here: look further
	jr	rr_s1
rr_s2:	cp	C_LADDER
	jr	c,rr_copy
	call	mul3		; index = look*3 + depth-1
	add	a,b
	dec	a
	ld	hl,(gSPR)
	call	ptrtab
	ld	a,h
	or	l
	call	nz,sprite
; copy the finished view to the screen (unless noshow is set)
rr_copy:
	call	kpoll
	ld	a,(noshow)
	or	a
	ret	nz
showbuf:
	ld	hl,VBUF
	ld	de,VIDEO
	ld	b,16
rc_1:	push	bc
	ld	bc,32
	ldir
	ex	de,hl
	ld	bc,32
	add	hl,bc
	ex	de,hl
	pop	bc
	djnz	rc_1
	ret

sidestate:			; B = depth, C = 0 left / 2 right -> A = segment state
	ld	a,b		; 2 = wall, 1 = opening with wall face, 0 = opening
	call	mul3
	add	a,c
	push	af
	call	markv
	pop	af
	call	cellv
	call	solid
	ld	a,2
	ret	z
	ld	a,b
	inc	a
	call	mul3
	add	a,c
	push	af
	call	markv
	pop	af
	call	cellv
	call	solid
	ld	a,1
	ret	z
	xor	a
	ret

blit:				; copy segment HL (col, width, 16 rows) into VBUF
	ld	e,(hl)		; width + 80H means blank: fill instead of copy
	inc	hl
	ld	c,(hl)
	inc	hl
	push	hl
	ld	hl,VBUF
	ld	d,0
	add	hl,de
	ex	de,hl
	pop	hl
	bit	7,c
	jr	nz,bl_fill
	ld	b,16
bl_1:	push	bc
	push	de
	ld	b,0
	ldir
	pop	de
	ex	de,hl
	ld	bc,32
	add	hl,bc
	ex	de,hl
	pop	bc
	djnz	bl_1
	ret
bl_fill:
	res	7,c
	ex	de,hl		; HL = destination
	ld	b,16
bl_f1:	push	bc
	push	hl
	ld	b,c
bl_f2:	ld	(hl),80h
	inc	hl
	djnz	bl_f2
	pop	hl
	ld	de,32
	add	hl,de
	pop	bc
	djnz	bl_f1
	ret

; ---- animation ------------------------------------------------------------
fdelay:				; pause between animation frames, queueing any keys
	ld	b,16
fd_1:	push	bc
	call	inkey
	or	a
	call	nz,kpush
	ld	bc,FRAMEDLY
fd_2:	dec	bc
	ld	a,b
	or	c
	jr	nz,fd_2
	pop	bc
	djnz	fd_1
	ret

kpoll:				; queue a key if one has just been pressed. The ROM routine
	call	inkey		; debounces and drops a key already released, so busy
	or	a		; code polls often to catch short taps early
	ret	z
kpush:				; add key A to the type-ahead queue (16 keys)
	push	hl
	ld	l,a
	ld	a,(kqn)
	cp	16
	jr	nc,kp_1
	ld	h,a
	ld	a,l
	ld	l,h
	ld	h,0
	push	de
	ld	de,KQ
	add	hl,de
	pop	de
	ld	(hl),a
	ld	hl,kqn
	inc	(hl)
kp_1:	pop	hl
	ret

animstep:			; show the view at phase A, then pause
	call	renderp
	jr	fdelay

turn:				; A = 0 turn right, 1 turn left, sliding the new view in
	ld	(tdir),a
	ld	hl,VBUF		; VBUF still holds the view on screen
	ld	de,OLDBUF
	ld	bc,32*16
	ldir
	ld	a,(tdir)
	or	a
	ld	a,(dir)
	jr	nz,tu_l
	inc	a
	jr	tu_s
tu_l:	add	a,3
tu_s:	and	3
	ld	(dir),a
	ld	a,1		; draw the new view off screen
	ld	(noshow),a
	call	render
	xor	a
	ld	(noshow),a
	ld	a,8		; three frames, sliding 8, 16, 24 columns
tu_f:	ld	(tshift),a
	call	slide
	call	fdelay
	ld	a,(tshift)
	add	a,8
	cp	32
	jr	c,tu_f
	jp	showbuf

slide:				; one turning frame: old view moves out, new view moves in
	ld	hl,0
	ld	(toff),hl
	ld	de,VIDEO
	ld	c,16
sl_row:	push	bc
	push	de
	ld	a,(tdir)
	or	a
	jr	nz,sl_left
	ld	hl,(toff)	; turning right: old[s..31] then new[0..s-1]
	ld	bc,OLDBUF
	add	hl,bc
	ld	a,(tshift)
	call	addhla
	call	sl_rest
	ld	hl,(toff)
	ld	bc,VBUF
	add	hl,bc
	call	sl_part
	jr	sl_next
sl_left:			; turning left: new[32-s..31] then old[0..31-s]
	ld	hl,(toff)
	ld	bc,VBUF
	add	hl,bc
	ld	a,(tshift)
	neg
	add	a,32
	call	addhla
	call	sl_part
	ld	hl,(toff)
	ld	bc,OLDBUF
	add	hl,bc
	call	sl_rest
sl_next:
	ld	hl,(toff)
	ld	bc,32
	add	hl,bc
	ld	(toff),hl
	pop	de
	ld	hl,64
	add	hl,de
	ex	de,hl
	pop	bc
	dec	c
	jr	nz,sl_row
	ret
sl_part:			; copy (tshift) bytes HL -> DE
	ld	a,(tshift)
	jr	sl_cp
sl_rest:			; copy 32-(tshift) bytes HL -> DE
	ld	a,(tshift)
	neg
	add	a,32
sl_cp:	ld	c,a
	ld	b,0
	ldir
	ret

sprite:				; mask sprite HL (col,row,w,h, mask/bits pairs) into VBUF
	ld	e,(hl)
	inc	hl
	ld	d,(hl)
	inc	hl
	ld	c,(hl)
	inc	hl
	ld	b,(hl)
	inc	hl
	push	hl
	ld	l,d
	ld	h,0
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	ld	d,0
	add	hl,de
	ld	de,VBUF
	add	hl,de
	ex	de,hl
	pop	hl
sp_1:	push	bc
	push	de
sp_2:	ld	a,(hl)		; mask
	cpl
	ex	de,hl
	and	(hl)
	ex	de,hl
	inc	hl
	or	(hl)		; bits
	or	80h
	ld	(de),a
	inc	hl
	inc	de
	dec	c
	jr	nz,sp_2
	pop	de
	ex	de,hl
	ld	bc,32
	add	hl,bc
	ex	de,hl
	pop	bc
	djnz	sp_1
	ret

; ===========================================================================
;  Map window: 15 x 7 cells around the player, 2 characters per cell
; ===========================================================================
drawmap:
	ld	a,(py)
	sub	3
	ld	(my),a
	ld	hl,VIDEO+MAPCOL
	ld	(vptr),hl
	ld	c,0
dm_row:	ld	a,(px)
	sub	7
	ld	(mx),a
	ld	de,(vptr)
	ld	b,15
dm_col:	ld	a,c
	cp	3
	jr	nz,dm_n
	ld	a,b
	cp	8
	jr	nz,dm_n
	ld	a,(dir)		; the player, drawn as a pair of arrows
	ld	hl,ARROWS
	call	addhla
	ld	a,(hl)
	ld	(de),a
	inc	de
	ld	(de),a
	inc	de
	jr	dm_next
dm_n:	ld	a,(mx)
	cp	32
	jr	nc,dm_blank
	ld	a,(my)
	cp	24
	jr	nc,dm_blank
	push	bc
	push	de
	ld	e,a		; (my)
	ld	d,a
	ld	a,(mx)
	ld	e,a
	call	caddr
	pop	de
	pop	bc
	ld	a,(hl)
	bit	7,a
	jr	z,dm_blank
	and	1Fh		; the look decides the map symbol
	add	a,a
	ld	hl,MAPCH
	call	addhla
	ld	a,(hl)
	ld	(de),a
	inc	de
	inc	hl
	ld	a,(hl)
	ld	(de),a
	inc	de
	jr	dm_next
dm_blank:
	ld	a,20h
	ld	(de),a
	inc	de
	ld	(de),a
	inc	de
dm_next:
	ld	hl,mx
	inc	(hl)
	djnz	dm_col
	push	bc
	call	kpoll		; don't miss key taps while drawing
	pop	bc
	ld	hl,(vptr)
	ld	de,64
	add	hl,de
	ld	(vptr),hl
	ld	hl,my
	inc	(hl)
	inc	c
	ld	a,c
	cp	7
	jp	c,dm_row
	ret

; ===========================================================================
;  Status line (row 7 of the right-hand side)
; ===========================================================================
status:	ld	hl,VIDEO+STROW*64+33
	ld	b,31
st_1:	ld	(hl),8Ch
	inc	hl
	djnz	st_1
	ld	a,1
	ld	(omode),a
	ld	hl,VIDEO+STROW*64+34
	ld	(vptr),hl
	ld	a,'L'
	call	putc
	ld	a,(level)
	inc	a
	call	putdeca
	ld	hl,S_HP
	call	msgc
	ld	a,(hp)
	call	putdeca
	ld	a,(statctr)	; the pack's chosen counter, e.g. " $:15"
	cp	0FFh
	jr	z,st_2
	ld	a,' '
	call	putc
	ld	a,(statlbl)
	call	putc
	ld	a,(statlbl+1)
	call	putc
	ld	a,(statctr)
	and	15
	add	a,a
	ld	hl,COUNTERS
	call	addhla
	ld	a,(hl)
	inc	hl
	ld	h,(hl)
	ld	l,a
	call	putdec
st_2:	ld	hl,S_SC
	call	msgc
	ld	hl,(points)
	call	putdec
	ld	a,' '
	call	putc
	ld	a,(dir)
	ld	hl,DIRL
	call	addhla
	ld	a,(hl)
	call	putc
	ld	a,' '
	call	putc
	xor	a
	ld	(omode),a
	ret

; ===========================================================================
;  Command input (row 15).  Arrow keys act at once when the line is empty;
;  otherwise letters build a command that ENTER executes.
; ===========================================================================
command:
	xor	a
	ld	(inlen),a
cm_0:	call	drawin
cm_1:	call	waitkey
	cp	0Dh
	jp	z,cm_ent
	cp	08h
	jr	z,cm_lt
	cp	09h
	jr	z,cm_rt
	cp	5Bh
	jr	z,cm_up
	cp	0Ah
	jr	z,cm_dn
	cp	1Fh
	jr	z,cm_clr
	cp	18h
	jr	z,cm_clr
	cp	'a'
	jr	c,cm_2
	cp	'z'+1
	jr	nc,cm_1
	sub	20h
cm_2:	cp	20h
	jr	c,cm_1
	cp	5Bh
	jr	nc,cm_1
	ld	b,a
	ld	a,(inlen)
	cp	26
	jr	nc,cm_1
	ld	hl,INBUF
	call	addhla
	ld	(hl),b
	ld	hl,inlen
	inc	(hl)
	jr	cm_0
cm_lt:	ld	a,(inlen)
	or	a
	jr	nz,cm_bs
	call	act_left
	jp	gturn
cm_bs:	dec	a
	ld	(inlen),a
	jr	cm_0
cm_rt:	ld	a,(inlen)
	or	a
	jr	nz,cm_1
	call	act_right
	jp	gturn
cm_up:	ld	a,(inlen)
	or	a
	jr	nz,cm_1
	call	act_fwd
	jp	gturn
cm_dn:	ld	a,(inlen)
	or	a
	jr	nz,cm_1
	call	act_back
	jp	gturn
cm_clr:	xor	a
	ld	(inlen),a
	jp	cm_0
cm_ent:	ld	a,(inlen)
	or	a
	jp	z,cm_1
	ld	hl,INBUF
	call	addhla
	ld	(hl),0
	ld	hl,M_ECHO
	call	msg
	ld	hl,INBUF
	call	msgc
	xor	a
	ld	(inlen),a
	call	drawin
	jp	exec

drawin:	ld	hl,VIDEO+15*64+TXTCOL
	ld	(hl),'>'
	inc	hl
	ld	(hl),' '
	inc	hl
	ld	de,INBUF
	ld	a,(inlen)
	or	a
	jr	z,di_2
	ld	b,a
di_1:	ld	a,(de)
	ld	(hl),a
	inc	hl
	inc	de
	djnz	di_1
di_2:	ld	(hl),0B0h	; cursor
	inc	hl
di_3:	ld	a,h
	cp	40h
	ret	nc
	ld	(hl),20h
	inc	hl
	jr	di_3

; ---- parser ---------------------------------------------------------------
skipsp:	ld	a,(hl)
	cp	' '
	ret	nz
	inc	hl
	jr	skipsp

exec:	ld	hl,INBUF
	call	skipsp
	ld	a,(hl)
	or	a
	ret	z
	ld	(wordp),hl
ex_1:	ld	a,(hl)		; find end of verb
	or	a
	jr	z,ex_3
	cp	' '
	jr	z,ex_2
	inc	hl
	jr	ex_1
ex_2:	ld	(hl),0
	inc	hl
	call	skipsp
ex_3:	ld	(nounp),hl
	ld	c,2		; the noun, if the script knows it
	call	findword
	ld	(nounid),a
	ld	de,VOCAB	; the verb: built-in first
ex_4:	ld	a,(de)
	or	a
	jr	z,ex_unk
	ld	hl,(wordp)
ex_5:	ld	a,(de)
	cp	(hl)
	jr	nz,ex_6
	or	a
	jr	z,ex_hit
	inc	de
	inc	hl
	jr	ex_5
ex_6:	ld	a,(de)		; skip rest of this entry
	inc	de
	or	a
	jr	nz,ex_6
	inc	de
	inc	de
	inc	de
	inc	de
	jr	ex_4
ex_hit:	inc	de
	ld	a,(de)		; turn flag
	ld	(tflag),a
	inc	de
	ld	a,(de)		; verb id
	ld	(verbid),a
	inc	de
	ld	a,(de)
	ld	l,a
	inc	de
	ld	a,(de)
	ld	h,a
	ld	(vhandler),hl
	jr	ex_rules
ex_unk:	ld	hl,(wordp)	; then the script's own verbs
	ld	c,1
	call	findword
	or	a
	jr	z,ex_bad
	ld	(verbid),a
	cp	32
	jr	nc,ex_cust
	call	bverb		; a script synonym of a built-in verb
	jr	ex_rules
ex_cust:
	ld	a,1
	ld	(tflag),a
	ld	hl,0
	ld	(vhandler),hl
ex_rules:
	call	runverb		; the level's rules get the first say
	cp	2
	jp	z,gturn
	cp	3
	jr	z,ex_do
	ld	hl,(vhandler)
	ld	a,h
	or	l
	jr	nz,ex_do
	ld	a,(rres)	; a script verb that did nothing
	or	a
	ld	hl,M_UNK
	jp	z,msg
	ld	hl,M_CANT
	jp	msg
ex_do:	ld	hl,(vhandler)
	ld	a,h
	or	l
	jp	z,gturn
	ld	de,ex_ret
	push	de
	jp	(hl)
ex_ret:	ld	a,(tflag)
	or	a
	ret	z
	jp	gturn
ex_bad:	ld	hl,M_UNK
	jp	msg

; ===========================================================================
;  Actions
; ===========================================================================
act_left:
	ld	a,1
	jp	turn
act_right:
	xor	a
	jp	turn
act_around:
	call	act_right
	jr	act_right
act_turn:
	ld	hl,(nounp)
	ld	a,(hl)
	cp	'L'
	jr	z,act_left
	cp	'R'
	jr	z,act_right
	jr	act_around

act_fwd:
	xor	a
	ld	(mvback),a
	ld	a,(fx)
	ld	b,a
	ld	a,(fy)
	ld	c,a
	jp	domove
act_back:
	ld	a,1
	ld	(mvback),a
	ld	a,(fx)
	neg
	ld	b,a
	ld	a,(fy)
	neg
	ld	c,a
	jp	domove

; ---- speech over the 3D view ----------------------------------------------------
; A character's speech is a list of short sentences separated by 01, each one
; pre-wrapped by ZorEdit into lines of up to 28 characters (separated by 13).
; Each sentence appears on its own, with no box around it. Word by word, each
; word comes out of the speaker's mouth and drifts up into its place in the
; sentence above their head. A finished sentence stays up for a moment, then
; the next one replaces it. Any key hurries things along. The last sentence
; stays until the next action.
MOUTHROW equ	8		; where words start: the mouth of a character
MOUTHCOL equ	16		;   standing right in front of the player
FLYDLY	equ	3		; pause per step of a flying word (units of about 10 ms)
WORDDLY	equ	2		; pause after each word lands
SENTDLY	equ	160		; how long a finished sentence stays up

speak:				; HL = speech
	push	hl
	call	msg		; the whole speech also goes in the text window
	pop	hl
sk_next:
	ld	(sptxt),hl
	xor	a
	ld	(skfast),a
	ld	a,1		; a fresh view, off screen
	ld	(noshow),a
	call	render
	xor	a
	ld	(noshow),a
	call	showbuf
	ld	hl,VBUF
	ld	(skrow),hl
	ld	hl,(sptxt)
sk_line:			; HL = start of a line: how long is it?
	push	hl
	ld	b,0
sk_len:	ld	a,(hl)
	cp	3		; 0, 1 (next sentence) and 2 (end of part) end a line
	jr	c,sk_l2
	cp	0Dh
	jr	z,sk_l2
	inc	b
	inc	hl
	jr	sk_len
sk_l2:	ld	a,28		; centre it: column 2 + (28 - length) / 2
	sub	b
	srl	a
	add	a,2
	ld	c,a
	ld	hl,(skrow)	; clear a strip one wider each side
	dec	a
	call	addhla
	ld	a,b
	add	a,2
	ld	e,a
sk_clr:	ld	(hl),20h
	inc	hl
	dec	e
	jr	nz,sk_clr
	ld	hl,(skrow)
	ld	a,c
	call	addhla
	ex	de,hl		; DE = where the words go
	pop	hl
sk_ch:	ld	a,(hl)
	cp	3
	jr	c,sk_end
	cp	0Dh
	jr	z,sk_eol
	cp	' '
	jr	nz,sk_wd
	ld	(de),a		; spaces just take their place
	inc	de
	inc	hl
	jr	sk_ch
sk_wd:	push	hl		; a word: how long is it?
	ld	b,0
sk_wl:	ld	a,(hl)
	cp	' '+1		; space, 13, 1 and 0 all end it
	jr	c,sk_wl2
	inc	b
	inc	hl
	jr	sk_wl
sk_wl2:	pop	hl
	call	fly		; out of the mouth and up into place
sk_wc:	ld	a,(hl)		; then it stays there
	ld	(de),a
	inc	hl
	inc	de
	djnz	sk_wc
	call	sk_word
	jr	sk_ch
sk_eol:	inc	hl
	push	hl
	ld	hl,(skrow)
	ld	bc,32
	add	hl,bc
	ld	(skrow),hl
	pop	hl
	jr	sk_line
sk_end:	push	hl		; the sentence is complete
	call	showbuf
	pop	hl
	ld	a,(hl)
	cp	1
	ret	nz		; the last sentence (0 or 2 follows) stays up
	inc	hl		; let this one be read, then the next
	push	hl
	ld	b,SENTDLY
	call	sdelay
	pop	hl
	jp	sk_next

sk_word:			; show the sentence so far, then pause (unless hurried)
	push	bc
	push	de
	push	hl
	call	showbuf
	ld	a,(skfast)
	or	a
	jr	nz,sw_1
	ld	b,WORDDLY
	call	sdelay
	jr	z,sw_1
	ld	a,1		; a key: show the rest of this sentence at once
	ld	(skfast),a
sw_1:	pop	hl
	pop	de
	pop	bc
	ret

fly:				; animate word HL (B letters) from the mouth to DE in VBUF
	ld	a,(skfast)
	or	a
	ret	nz
	push	bc
	push	de
	push	hl
	ld	(fword),hl
	ld	a,b
	ld	(flen),a
	ex	de,hl		; target row and column from the VBUF address
	ld	de,VBUF
	or	a
	sbc	hl,de
	ld	a,l
	and	31
	ld	(ftc),a
	add	hl,hl
	add	hl,hl
	add	hl,hl
	ld	a,h
	ld	(ftr),a
	ld	a,(flen)	; start: centred on the mouth
	srl	a
	ld	b,a
	ld	a,MOUTHCOL
	sub	b
	ld	(fcol),a
	ld	a,MOUTHROW
	ld	(frow),a
fl_1:	call	showbuf		; the view and the words already in place
	call	fl_draw		; the word in flight, straight onto the screen
	ld	b,FLYDLY
	call	sdelay
	jr	nz,fl_hurry
	ld	a,(ftr)		; arrived?
	ld	b,a
	ld	a,(frow)
	sub	b
	jr	z,fl_done
	jr	c,fl_done
	ld	b,a		; B = steps left; move the column 1/B of the way
	ld	a,(fcol)
	ld	c,a
	ld	a,(ftc)
	sub	c
	jr	c,fl_left
	call	fl_div
	add	a,c
	jr	fl_set
fl_left:
	neg
	call	fl_div
	ld	d,a
	ld	a,c
	sub	d
fl_set:	ld	(fcol),a
	ld	hl,frow
	dec	(hl)
	jr	fl_1
fl_hurry:
	ld	a,1
	ld	(skfast),a
fl_done:
	pop	hl
	pop	de
	pop	bc
	ret

fl_div:				; A = A / B
	ld	d,0
fd_d1:	cp	b
	jr	c,fd_d2
	sub	b
	inc	d
	jr	fd_d1
fd_d2:	ld	a,d
	ret

fl_draw:			; draw the flying word at (frow, fcol) in video RAM
	ld	a,(frow)
	ld	l,a
	ld	h,0
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	add	hl,hl
	ld	de,VIDEO-1
	add	hl,de
	ld	a,(fcol)
	call	addhla
	ld	(hl),20h	; a clear space each side
	inc	hl
	ex	de,hl
	ld	hl,(fword)
	ld	a,(flen)
	ld	c,a
	ld	b,0
	ldir
	ex	de,hl
	ld	(hl),20h
	ret

sdelay:				; wait B x 10 ms; NZ (and stop) if a key is pressed
	call	inkey
	or	a
	ret	nz
	push	bc
	ld	bc,640
sd_1:	dec	bc
	ld	a,b
	or	c
	jr	nz,sd_1
	pop	bc
	djnz	sdelay
	xor	a
	ret

; ===========================================================================
;  Tables
; ===========================================================================
DXT:	db	0,1,0,-1	; dx for N E S W
DYT:	db	-1,0,1,0	; dy (getvec reads DXT+4)
ARROWS:	db	5Bh,5Eh,5Ch,5Dh	; Model I up/right/down/left arrow glyphs
DIRL:	db	"NESW"
DIRNAME: dw	D_N,D_E,D_S,D_W
D_N:	db	"NORTH",0
D_E:	db	"EAST",0
D_S:	db	"SOUTH",0
D_W:	db	"WEST",0

; map characters, two per cell code
MAPCH:	db	88h,80h		; floor (faint dot)
	db	0BFh,0BFh	; wall
	db	"##"		; door
	db	"H",80h		; ladder
	db	"G",80h		; guardian
	db	"K",80h		; key
	db	"S",80h		; sword
	db	"P",80h		; potion
	db	"$",80h		; gold
	db	"A",80h		; amulet
	db	"?",80h		; sign
	db	"*",80h		; trap (once seen)
	db	"@",80h		; teleporter
	db	"W",80h		; fountain
	db	"=",80h		; pressure plate
	db	0BFh,0BFh	; secret wall (looks like wall)
	db	"&",80h		; hermit
	db	"&",80h		; merchant
	db	"&",80h		; ghost
	db	"+",80h		; chest


	include	rules.asm


	include	gfx.inc

; ===========================================================================
;  Variables
; ===========================================================================
seed:	ds	2
px:	ds	1
py:	ds	1
dir:	ds	1
fx:	ds	1
fy:	ds	1
lfx:	ds	1
lfy:	ds	1
cx:	ds	1
cy:	ds	1
mx:	ds	1
my:	ds	1
front:	ds	1
kmax:	ds	1
segi:	ds	1
hp:	ds	1
skfast:	ds	1		; 1 = finish the sentence at once
sptxt:	ds	2
skrow:	ds	2
fword:	ds	2		; the word in flight
flen:	ds	1
frow:	ds	1
fcol:	ds	1
ftr:	ds	1
ftc:	ds	1
keepview: ds	1
verbid:	ds	1		; the command being obeyed
nounid:	ds	1
vhandler: ds	2
rres:	ds	1		; rules: 0 none had this verb, 1 none applied, 2 one acted
contflag: ds	1
stopflag: ds	1		; a rule said stop
idirty:	ds	1		; objects moved: the maze bits need refreshing
rverb:	ds	1		; the rules being run: verb/trigger, scope, mode
rscope:	ds	1
rmode:	ds	1		; 0 = the first rule that acts ends it, 1 = all
rbit:	ds	1		; TMASK bit of the trigger being run
rinst:	ds	1
rptr:	ds	2
thisi:	ds	1		; THIS: the object a rule is about (0FFh = none)
cop:	ds	1		; condition/action being evaluated: op, a, b, c
ca:	ds	1
cb:	ds	1
cc:	ds	1
fitem:	ds	1		; scratch for the object searches
ftyp:	ds	1
fstate:	ds	1
fmatch:	ds	1
fcount:	ds	1
nprint:	ds	1
moved:	ds	1		; a rule moved the player (goto)
tele:	ds	1
mkany:	ds	1		; 1 = mkcands ignores the noun (objects in front)
ncand:	ds	1		; objects a command may be about, nearest first
CANDS:	ds	MAXINST
ninst:	ds	1		; objects on this level
INST:	ds	MAXINST*8	; x, y, type, value, p1, p2, text
nkeep:	ds	1		; objects carried on to the next level
KEEPBUF: ds	16*8
TMASK:	ds	MAXTYPES	; per type: bit 0 every turn, 1 arrive, 2 bump, 3 start rules
FLAGS:	ds	4		; 32 script flags (whole game)
COUNTERS: ds	32		; 16 script counters, 16-bit (must follow FLAGS)
points:	ds	2
vocabp:	ds	2
typep:	ds	2
prules:	ds	2
ptexts:	ds	2
lrules:	ds	2
ltexts:	ds	2
ctexts:	ds	2		; texts of the rules being run
level:	ds	1
starthp: ds	1
maxhp:	ds	1
statctr: ds	1
statlbl: ds	2
lvname:	ds	2
lvintro: ds	2
tailp:	ds	2
packp:	ds	2
packbuf: ds	2
dest:	ds	2
errcode: ds	1
noshow:	ds	1		; 1 = render into VBUF only
kqn:	ds	1		; keys waiting in KQ
KQ:	ds	16
mvback:	ds	1
tdir:	ds	1
tshift:	ds	1
toff:	ds	2
gSEGL:	ds	2		; tables for the phase being drawn (copied from PHASETAB)
gSEGR:	ds	2
gSEGF:	ds	2
gF5:	ds	2
gSPR:	ds	2
OLDBUF:	ds	32*16
FCB:	ds	50
SECBUF:	ds	256
moves:	ds	2
omode:	ds	1
vptr:	ds	2
trow:	ds	1
tcol:	ds	1
inlen:	ds	1
tflag:	ds	1
wordp:	ds	2
nounp:	ds	2
INBUF:	ds	32
VA:	ds	30
VBUF:	ds	32*16
MAZE:	ds	32*24
	ds	256
STACK:

; ---------------------------------------------------------------------------
;  Built-in level pack. ZorEdit replaces everything from here on when it
;  exports a game .CMD; this empty placeholder (no levels) makes the game
;  load ZOR/LEV from disk instead.
; ---------------------------------------------------------------------------
BUILTIN:
	db	"ZORPACK",5
	dw	PK_LEVS
	db	0
	ds	PK_LEVS-11

	end	entry
